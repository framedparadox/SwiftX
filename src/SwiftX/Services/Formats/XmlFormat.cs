using System.Xml;
using System.Xml.Linq;
using SwiftX.Models;
using SwiftX.Services.Syntax;

namespace SwiftX.Services.Formats;

/// <summary>
/// The XML format. Every reader is XXE- and entity-expansion-hardened (design doc §21):
/// <see cref="XmlReaderSettings.DtdProcessing"/> is <see cref="DtdProcessing.Prohibit"/> and
/// <see cref="XmlReaderSettings.XmlResolver"/> is null, which blocks both classic XXE (reading
/// local files via an external entity) and billion-laughs expansion by refusing any DOCTYPE at
/// all. This is enforced by <c>SwiftX.Tests.Formats.XmlFormatSecurityTests</c>, not by convention.
/// </summary>
public sealed class XmlFormat : IDataFormat
{
    public static readonly XmlFormat Instance = new();

    public string Id => "xml";
    public string DisplayNameKey => "Format.Xml";
    public string[] Extensions => [".xml"];
    public ITokenizer Tokenizer => XmlTokenizer.Instance;

    public int DetectConfidence(ReadOnlySpan<char> text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > 0 && trimmed[0] == '<' ? 80 : 0;
    }

    public FormatResult Format(string text, FormatOptions options)
    {
        try
        {
            var doc = LoadSecure(text);
            using var sw = new StringWriter();
            using (var writer = XmlWriter.Create(sw, WriterSettings(indent: true, options.IndentWidth)))
                doc.Save(writer);
            return FormatResult.Ok(sw.ToString());
        }
        catch (XmlException ex)
        {
            return FormatResult.Fail(ToDiagnostic(ex));
        }
    }

    public FormatResult Minify(string text)
    {
        try
        {
            var doc = LoadSecure(text);
            using var sw = new StringWriter();
            using (var writer = XmlWriter.Create(sw, WriterSettings(indent: false, 0)))
                doc.Save(writer);
            return FormatResult.Ok(sw.ToString());
        }
        catch (XmlException ex)
        {
            return FormatResult.Fail(ToDiagnostic(ex));
        }
    }

    public IReadOnlyList<Diagnostic> Validate(string text)
    {
        try
        {
            LoadSecure(text);
            return [];
        }
        catch (XmlException ex)
        {
            return [ToDiagnostic(ex)];
        }
    }

    public DataNode ToCanonical(string text)
    {
        var doc = LoadSecure(text);
        var root = doc.Root ?? throw new XmlException("Document has no root element.");
        return new ObjectNode([(root.Name.LocalName, ConvertElement(root))]);
    }

    public string FromCanonical(DataNode node, FormatOptions options)
    {
        string rootName = "root";
        DataNode content = node;
        if (node is ObjectNode { Members.Count: 1 } obj)
        {
            rootName = obj.Members[0].Key;
            content = obj.Members[0].Value;
        }

        var root = BuildElement(rootName, content);
        using var sw = new StringWriter();
        using (var writer = XmlWriter.Create(sw, WriterSettings(indent: true, options.IndentWidth)))
            new XDocument(root).Save(writer);
        return sw.ToString();
    }

    // ---- Internals ----------------------------------------------------------------------

    /// <summary>The one place an <see cref="XmlReader"/> is constructed for untrusted text —
    /// hardened against XXE and billion-laughs by refusing DTDs entirely.
    /// <para>
    /// <see cref="XmlReaderSettings.IgnoreWhitespace"/> is deliberately true: most real input has
    /// already been pretty-printed by something else, so the whitespace-only text nodes between
    /// tags are exactly what Format has to replace with the requested indent and Minify has to
    /// remove. <see cref="XmlWriter"/>'s own <see cref="XmlWriterSettings.Indent"/> logic stops
    /// indenting an element the moment it sees the element already has a text-node child, so with
    /// this left false (or with <see cref="XDocument"/>'s own <c>LoadOptions.PreserveWhitespace</c>
    /// — which does not override a reader supplied already-constructed, and so was a no-op here)
    /// that whitespace survived onto the loaded tree and silently turned both commands into no-ops
    /// on any input that was not already on one line. This setting only discards whitespace-only
    /// nodes between tags; a text node with real content (including one that is entirely spaces
    /// around non-whitespace text, e.g. <c>&lt;a&gt; hello &lt;/a&gt;</c>) is never whitespace-only,
    /// so it is unaffected and round-trips exactly.
    /// </para></summary>
    private static XDocument LoadSecure(string text)
    {
        using var stringReader = new StringReader(text);
        using var reader = XmlReader.Create(stringReader, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            IgnoreWhitespace = true,
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        EnsureWithinDepthLimit(document);
        return document;
    }

    /// <summary>
    /// The nesting ceiling, deliberately the same number as <see cref="JsonFormat"/>'s
    /// <see cref="System.Text.Json.JsonDocumentOptions.MaxDepth"/> so the two formats refuse the
    /// same shape of input. Real documents nest tens of levels, not hundreds.
    /// </summary>
    public const int MaxDepth = 256;

    /// <summary>
    /// Refuses a document nested past <see cref="MaxDepth"/>, which is §22's "never an OOM, never
    /// a hang" contract applied to nesting rather than to length. Two separate failures made this
    /// load-bearing rather than defensive, and both are reachable from a small input:
    /// <list type="number">
    /// <item><b>Stack overflow.</b> <see cref="ConvertElement"/> recurses once per level, so
    /// Convert-to-JSON on a document a few thousand deep overflowed the stack — and a
    /// <see cref="StackOverflowException"/> cannot be caught, so the process died outright rather
    /// than reporting a bad document. <c>&lt;a&gt;</c> repeated 4000 times (24 KB of text) was
    /// enough.</item>
    /// <item><b>Out of memory.</b> Indentation is quadratic in depth — level <c>n</c> costs
    /// <c>n</c> indent characters on every line below it — so Format on a 50000-deep document
    /// (350 KB of text, comfortably inside <see cref="Services.InputLimits.MaxTextBytes"/>) tried
    /// to build a multi-gigabyte string and threw <see cref="OutOfMemoryException"/>, which the
    /// <see cref="XmlException"/> handlers around it do not catch.</item>
    /// </list>
    /// <para>
    /// Enforced here, on the one shared load path, so all four public entry points
    /// (Format/Minify/Validate/ToCanonical) inherit it. The walk is iterative on an explicit
    /// stack: recursing to measure the nesting would be the very thing being guarded against.
    /// </para>
    /// </summary>
    private static void EnsureWithinDepthLimit(XDocument document)
    {
        if (document.Root is null)
            return;

        var pending = new Stack<(XElement Element, int Depth)>();
        pending.Push((document.Root, 1));
        while (pending.Count > 0)
        {
            var (element, depth) = pending.Pop();
            if (depth > MaxDepth)
                throw new XmlException($"Document is nested more than {MaxDepth} levels deep.");
            foreach (var child in element.Elements())
                pending.Push((child, depth + 1));
        }
    }

    private static XmlWriterSettings WriterSettings(bool indent, int indentWidth) => new()
    {
        Indent = indent,
        IndentChars = new string(' ', Math.Clamp(indentWidth <= 0 ? 2 : indentWidth, 1, 8)),
        OmitXmlDeclaration = true,
        NewLineOnAttributes = false,
    };

    private static Diagnostic ToDiagnostic(XmlException ex) => new(ex.LineNumber, ex.LinePosition, ex.Message);

    /// <summary>
    /// Repeated sibling elements collapse into an <see cref="ArrayNode"/> (the conventional
    /// XML→JSON shape); a leaf with no attributes becomes a bare <see cref="ScalarNode"/> so a
    /// simple <c>&lt;name&gt;Ada&lt;/name&gt;</c> round-trips as the string <c>"Ada"</c> rather
    /// than an object wrapper.
    /// </summary>
    private static DataNode ConvertElement(XElement element)
    {
        var members = new List<(string Key, DataNode Value)>();
        foreach (var attr in element.Attributes())
        {
            if (!attr.IsNamespaceDeclaration)
                members.Add(("@" + attr.Name.LocalName, new ScalarNode(attr.Value, ScalarKind.String)));
        }

        var childElements = element.Elements().ToList();
        if (childElements.Count == 0)
        {
            if (members.Count == 0)
                return new ScalarNode(element.Value, ScalarKind.String);
            members.Add(("#text", new ScalarNode(element.Value, ScalarKind.String)));
            return new ObjectNode(members);
        }

        var order = new List<string>();
        var groups = new Dictionary<string, List<DataNode>>();
        foreach (var child in childElements)
        {
            var name = child.Name.LocalName;
            if (!groups.TryGetValue(name, out var list))
            {
                list = [];
                groups[name] = list;
                order.Add(name);
            }
            list.Add(ConvertElement(child));
        }
        foreach (var name in order)
        {
            var list = groups[name];
            members.Add((name, list.Count > 1 ? new ArrayNode(list) : list[0]));
        }
        return new ObjectNode(members);
    }

    /// <summary>
    /// The write-side companion to <see cref="EnsureWithinDepthLimit"/>. Every
    /// <see cref="DataNode"/> reaching here is produced by a <c>ToCanonical</c> that is itself
    /// depth-capped (XML by that guard, JSON by its parser's <c>MaxDepth</c>), so this is the
    /// belt to that pair of braces rather than the primary limit — but it is what keeps the
    /// recursion below provably bounded no matter where a tree came from.
    /// </summary>
    private static XElement BuildElement(string name, DataNode node, int depth = 1)
    {
        if (depth > MaxDepth)
            throw new XmlException($"Document is nested more than {MaxDepth} levels deep.");

        var el = new XElement(name);
        switch (node)
        {
            case ScalarNode s:
                el.Value = s.Kind == ScalarKind.Null ? "" : s.Raw ?? "";
                break;
            case ArrayNode arr:
                // Reached only for an array with no member name of its own (e.g. the canonical
                // root is itself an array); "item" is the least-surprising synthetic tag.
                foreach (var item in arr.Items)
                    el.Add(BuildElement("item", item, depth + 1));
                break;
            case ObjectNode obj:
                foreach (var (key, value) in obj.Members)
                {
                    if (key.StartsWith('@'))
                        el.SetAttributeValue(key[1..], (value as ScalarNode)?.Raw ?? "");
                    else if (key == "#text")
                        el.Value = (value as ScalarNode)?.Raw ?? "";
                    else if (value is ArrayNode valueArray)
                        foreach (var item in valueArray.Items)
                            el.Add(BuildElement(key, item, depth + 1));
                    else
                        el.Add(BuildElement(key, value, depth + 1));
                }
                break;
        }
        return el;
    }
}
