using System.Text;
using SwiftX.Models;
using SwiftX.Services.Syntax;

namespace SwiftX.Services.Formats;

/// <summary>
/// A hand-rolled YAML 1.2-ish format covering the common surface: block mappings and sequences
/// (2-space-multiple indentation), flow mappings/sequences (<c>{...}</c> / <c>[...]</c>), single-
/// and double-quoted and plain scalars, and <c>#</c> comments.
/// <para>
/// <b>Deliberately not supported</b> (the same "lightweight tool, not a spec-complete engine"
/// trade-off <see cref="XmlFormat"/> and <see cref="JsonFormat"/> make with their own depth caps):
/// anchors/aliases (<c>&amp;</c>/<c>*</c>/<c>&lt;&lt;</c>), block scalars (<c>|</c>/<c>&gt;</c>),
/// tags (<c>!!str</c>), multi-document streams, and a sequence item written as a bare nested dash
/// (<c>- - a</c>) rather than <c>-</c> on its own line. None of these crash — see the type-level
/// contract every format in this app follows (never throw uncaught, never silently corrupt data);
/// an anchor or a second <c>---</c> document surfaces as a <see cref="Diagnostic"/> naming exactly
/// what isn't supported, the same way <see cref="XmlFormat"/> surfaces a document past its depth
/// cap rather than silently truncating it.
/// </para>
/// </summary>
public sealed class YamlFormat : IDataFormat
{
    public static readonly YamlFormat Instance = new();

    public string Id => "yaml";
    public string DisplayNameKey => "Format.Yaml";
    public string[] Extensions => [".yaml", ".yml"];
    public ITokenizer Tokenizer => YamlTokenizer.Instance;

    /// <summary>Same ceiling as <see cref="XmlFormat.MaxDepth"/> and JSON's parser — real
    /// documents nest tens of levels, not hundreds; past this a stack-recursive parse or writer
    /// (both <see cref="ParseBlock"/> and <see cref="WriteBlock"/> recurse once per level) is a
    /// stack-overflow or OOM risk rather than a genuine document.</summary>
    public const int MaxDepth = 256;

    public int DetectConfidence(ReadOnlySpan<char> text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return 0;
        char first = trimmed[0];
        // A brace/bracket/angle-bracket lead is JSON's or XML's to claim (flow-style YAML is a
        // superset of JSON syntax) — see FormatRegistry.All's ordering note.
        if (first is '{' or '[' or '<') return 0;

        bool sawDocMarker = false;
        bool sawStructure = false;
        int nonBlankLines = 0;

        foreach (var lineSpan in trimmed.EnumerateLines())
        {
            var line = lineSpan.TrimStart(' ');
            if (line.Length == 0 || line[0] == '#')
                continue;
            nonBlankLines++;

            if (line.SequenceEqual("---") || HasPrefix(line, "--- "))
            {
                sawDocMarker = true;
                continue;
            }
            if (line[0] == '-' && (line.Length == 1 || line[1] == ' '))
            {
                sawStructure = true;
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon > 0 && (colon == line.Length - 1 || line[colon + 1] == ' '))
                sawStructure = true;
        }

        if (nonBlankLines == 0) return 0;
        if (sawDocMarker) return 70;
        return sawStructure ? 55 : 0;
    }

    private static bool HasPrefix(ReadOnlySpan<char> line, string prefix) =>
        line.Length >= prefix.Length && line[..prefix.Length].SequenceEqual(prefix);

    public FormatResult Format(string text, FormatOptions options)
    {
        try
        {
            return FormatResult.Ok(Write(ToCanonical(text), options));
        }
        catch (YamlParseException ex)
        {
            return FormatResult.Fail(new Diagnostic(ex.Line, ex.Column, ex.Message));
        }
    }

    /// <summary>
    /// YAML's flow style is JSON-compatible syntax, so "minified YAML" is just the document
    /// written as compact flow collections — which is exactly what <see cref="JsonFormat"/>'s own
    /// writer already produces. Reusing it here avoids a second, parallel compact-writer.
    /// </summary>
    public FormatResult Minify(string text)
    {
        try
        {
            var node = ToCanonical(text);
            var json = JsonFormat.Instance.FromCanonical(node, FormatOptions.Default);
            return JsonFormat.Instance.Minify(json);
        }
        catch (YamlParseException ex)
        {
            return FormatResult.Fail(new Diagnostic(ex.Line, ex.Column, ex.Message));
        }
    }

    public IReadOnlyList<Diagnostic> Validate(string text)
    {
        try
        {
            ToCanonical(text);
            return [];
        }
        catch (YamlParseException ex)
        {
            return [new Diagnostic(ex.Line, ex.Column, ex.Message)];
        }
    }

    public DataNode ToCanonical(string text)
    {
        var lines = BuildLines(text);
        if (lines.Count == 0)
            return new ScalarNode(null, ScalarKind.Null);

        var (node, next) = ParseBlock(lines, 0, 1);
        if (next != lines.Count)
        {
            var extra = lines[next];
            throw new YamlParseException(extra.Number, extra.Indent + 1, "Unexpected indentation.");
        }
        return node;
    }

    public string FromCanonical(DataNode node, FormatOptions options) => Write(node, options);

    // ---- Line preprocessing --------------------------------------------------------------

    private readonly record struct RawLine(int Number, int Indent, string Content);

    /// <summary>
    /// Splits the document into logical content lines: blank lines and whole-line comments are
    /// dropped, a leading <c>---</c> document marker is consumed, a <c>...</c> end-of-document
    /// marker stops reading (anything after it is ignored), and every remaining line has its
    /// indent measured and any trailing inline comment stripped. Tabs in the indentation are
    /// rejected outright — the same rule the YAML spec itself enforces, and untangling tab-based
    /// indentation into a column count is not a "lightweight tool" problem worth solving.
    /// </summary>
    private static List<RawLine> BuildLines(string text)
    {
        var result = new List<RawLine>();
        int i = 0;
        int number = 0;

        while (i <= text.Length)
        {
            number++;
            int lineEnd = text.IndexOf('\n', i);
            int rawEnd = lineEnd < 0 ? text.Length : lineEnd;
            int contentEnd = rawEnd > i && text[rawEnd - 1] == '\r' ? rawEnd - 1 : rawEnd;
            string raw = text[i..contentEnd];

            int indent = 0;
            while (indent < raw.Length && raw[indent] == ' ') indent++;
            if (indent < raw.Length && raw[indent] == '\t')
                throw new YamlParseException(number, indent + 1, "Tab characters are not allowed for indentation.");

            string content = raw[indent..];
            if (content.Length == 0 || content[0] == '#')
            {
                // Blank line or whole-line comment: nothing to keep.
            }
            else if (indent == 0 && (content == "---" || content.StartsWith("--- ", StringComparison.Ordinal)))
            {
                // A "---" before any real content is just the optional leading document marker;
                // one that shows up after content has already been collected is a second document
                // starting — that's the shape "multiple documents" actually needs to catch, not
                // merely "more than one marker anywhere in the file" (a bare marker line has
                // nothing after it to distinguish the two otherwise).
                if (result.Count > 0)
                    throw new YamlParseException(number, 1, "Multiple YAML documents are not supported.");
                string rest = content.Length > 3 ? StripInlineComment(content[3..].TrimStart()) : "";
                if (rest.Length > 0)
                    result.Add(new RawLine(number, 0, rest));
            }
            else if (indent == 0 && content == "...")
            {
                break;
            }
            else
            {
                string stripped = StripInlineComment(content);
                if (stripped.Length > 0)
                    result.Add(new RawLine(number, indent, stripped));
            }

            if (lineEnd < 0) break;
            i = lineEnd + 1;
        }

        return result;
    }

    /// <summary>Removes a trailing <c># comment</c> — only when the <c>#</c> is preceded by
    /// whitespace and sits outside any quoted span, so a value like <c>http://x#fragment</c>
    /// survives untouched.</summary>
    private static string StripInlineComment(string content)
    {
        bool inSingle = false, inDouble = false;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inDouble)
            {
                if (c == '\\' && i + 1 < content.Length) { i++; continue; }
                if (c == '"') inDouble = false;
                continue;
            }
            if (inSingle)
            {
                if (c == '\'')
                {
                    if (i + 1 < content.Length && content[i + 1] == '\'') { i++; continue; }
                    inSingle = false;
                }
                continue;
            }
            if (c == '"') { inDouble = true; continue; }
            if (c == '\'') { inSingle = true; continue; }
            if (c == '#' && (i == 0 || content[i - 1] == ' '))
                return content[..i].TrimEnd();
        }
        return content;
    }

    // ---- Block parsing ---------------------------------------------------------------------

    private static bool IsSequenceItem(string content) =>
        content.Length > 0 && content[0] == '-' && (content.Length == 1 || content[1] == ' ');

    private static (DataNode Node, int Next) ParseBlock(List<RawLine> lines, int start, int depth)
    {
        if (depth > MaxDepth)
            throw new YamlParseException(lines[start].Number, lines[start].Indent + 1,
                $"Document is nested more than {MaxDepth} levels deep.");

        return IsSequenceItem(lines[start].Content)
            ? ParseSequence(lines, start, depth)
            : ParseMapping(lines, start, depth);
    }

    private static (DataNode, int) ParseSequence(List<RawLine> lines, int start, int depth)
    {
        int seqIndent = lines[start].Indent;
        var items = new List<DataNode>();
        int i = start;

        while (i < lines.Count && lines[i].Indent == seqIndent && IsSequenceItem(lines[i].Content))
        {
            var line = lines[i];
            int afterDash = 1;
            while (afterDash < line.Content.Length && line.Content[afterDash] == ' ')
                afterDash++;
            string remainder = line.Content[afterDash..];
            int childIndent = line.Indent + afterDash;

            if (remainder.Length == 0)
            {
                if (i + 1 < lines.Count && lines[i + 1].Indent > seqIndent)
                {
                    var (child, next) = ParseBlock(lines, i + 1, depth + 1);
                    items.Add(child);
                    i = next;
                }
                else
                {
                    items.Add(new ScalarNode(null, ScalarKind.Null));
                    i++;
                }
                continue;
            }

            int colon = FindTopLevelColon(remainder);
            if (colon >= 0)
            {
                // "- key: value", possibly continued on the following lines aligned under "key".
                var combined = new List<RawLine> { new(line.Number, childIndent, remainder) };
                int j = i + 1;
                while (j < lines.Count && lines[j].Indent >= childIndent)
                {
                    combined.Add(lines[j]);
                    j++;
                }
                var (mapNode, consumed) = ParseMapping(combined, 0, depth + 1);
                if (consumed != combined.Count)
                {
                    var extra = combined[consumed];
                    throw new YamlParseException(extra.Number, extra.Indent + 1, "Unexpected indentation.");
                }
                items.Add(mapNode);
                i = j;
            }
            else
            {
                items.Add(ParseFlowOrScalar(remainder, line.Number, childIndent + 1, depth + 1));
                i++;
            }
        }

        return (new ArrayNode(items), i);
    }

    private static (DataNode, int) ParseMapping(List<RawLine> lines, int start, int depth)
    {
        int mapIndent = lines[start].Indent;
        var members = new List<(string, DataNode)>();
        int i = start;

        while (i < lines.Count && lines[i].Indent == mapIndent && !IsSequenceItem(lines[i].Content))
        {
            var line = lines[i];
            int colon = FindTopLevelColon(line.Content);
            if (colon < 0)
                throw new YamlParseException(line.Number, mapIndent + 1, "Expected 'key: value'.");

            string key = ParseScalarText(line.Content[..colon].TrimEnd());
            string valuePart = line.Content[(colon + 1)..].TrimStart();

            if (valuePart.Length == 0)
            {
                if (i + 1 < lines.Count && lines[i + 1].Indent > mapIndent)
                {
                    var (child, next) = ParseBlock(lines, i + 1, depth + 1);
                    members.Add((key, child));
                    i = next;
                }
                else
                {
                    members.Add((key, new ScalarNode(null, ScalarKind.Null)));
                    i++;
                }
            }
            else
            {
                int valueColumn = line.Indent + (line.Content.Length - valuePart.Length) + 1;
                members.Add((key, ParseFlowOrScalar(valuePart, line.Number, valueColumn, depth + 1)));
                i++;
            }
        }

        if (members.Count == 0)
            throw new YamlParseException(lines[start].Number, mapIndent + 1, "Expected a mapping.");

        return (new ObjectNode(members), i);
    }

    /// <summary>Finds the colon that separates a block mapping key from its value: the first
    /// <c>:</c> outside any quoted or flow-bracketed span, followed by a space or end of line.</summary>
    private static int FindTopLevelColon(string content)
    {
        bool inSingle = false, inDouble = false;
        int depth = 0;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inDouble)
            {
                if (c == '\\' && i + 1 < content.Length) { i++; continue; }
                if (c == '"') inDouble = false;
                continue;
            }
            if (inSingle)
            {
                if (c == '\'')
                {
                    if (i + 1 < content.Length && content[i + 1] == '\'') { i++; continue; }
                    inSingle = false;
                }
                continue;
            }
            switch (c)
            {
                case '"': inDouble = true; continue;
                case '\'': inSingle = true; continue;
                case '{': case '[': depth++; continue;
                case '}': case ']': if (depth > 0) depth--; continue;
                case ':' when depth == 0 && (i + 1 == content.Length || content[i + 1] == ' '):
                    return i;
            }
        }
        return -1;
    }

    private static string ParseScalarText(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return InputSanitizer.Unescape(raw[1..^1]);
        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
            return raw[1..^1].Replace("''", "'");
        return raw;
    }

    private static DataNode ParseFlowOrScalar(string text, int lineNumber, int column, int depth)
    {
        if (depth > MaxDepth)
            throw new YamlParseException(lineNumber, column, $"Document is nested more than {MaxDepth} levels deep.");

        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return new ScalarNode(null, ScalarKind.Null);

        if (trimmed[0] == '{' || trimmed[0] == '[')
        {
            var flow = new FlowParser(trimmed, lineNumber, column);
            var node = flow.ParseValue(depth);
            flow.SkipWs();
            if (!flow.AtEnd)
                throw flow.Error("Unexpected content after value.");
            return node;
        }

        if (trimmed[0] == '"')
        {
            if (trimmed.Length < 2 || trimmed[^1] != '"')
                throw new YamlParseException(lineNumber, column, "Unterminated quoted string.");
            return new ScalarNode(InputSanitizer.Unescape(trimmed[1..^1]), ScalarKind.String);
        }
        if (trimmed[0] == '\'')
        {
            if (trimmed.Length < 2 || trimmed[^1] != '\'')
                throw new YamlParseException(lineNumber, column, "Unterminated quoted string.");
            return new ScalarNode(trimmed[1..^1].Replace("''", "'"), ScalarKind.String);
        }

        return ParsePlainScalar(trimmed);
    }

    private static DataNode ParsePlainScalar(string trimmed)
    {
        if (trimmed == "null" || trimmed == "~")
            return new ScalarNode(null, ScalarKind.Null);
        // Only the lowercase, JSON-compatible spellings count as booleans (YAML 1.2's "core"
        // schema, not 1.1's much larger True/Yes/On/... set) — anything else is a plain string,
        // which keeps round-tripping through JSON unambiguous.
        if (trimmed == "true" || trimmed == "false")
            return new ScalarNode(trimmed, ScalarKind.Boolean);
        if (IsNumberLiteral(trimmed))
            return new ScalarNode(trimmed, ScalarKind.Number);
        return new ScalarNode(trimmed, ScalarKind.String);
    }

    private static bool IsNumberLiteral(string s)
    {
        int i = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        int digitsStart = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        if (i == digitsStart) return false;
        if (i < s.Length && s[i] == '.')
        {
            i++;
            int fracStart = i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
            if (i == fracStart) return false;
        }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            int expStart = i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
            if (i == expStart) return false;
        }
        return i == s.Length;
    }

    /// <summary>A small recursive-descent parser for one flow collection (<c>{...}</c> /
    /// <c>[...]</c>) — flow YAML is JSON-compatible syntax plus single-quoted strings and bare
    /// plain scalars, so this is a mini JSON parser with those two additions.</summary>
    private sealed class FlowParser(string text, int lineNumber, int baseColumn)
    {
        private int _pos;

        public bool AtEnd => _pos >= text.Length;

        public void SkipWs()
        {
            while (_pos < text.Length && text[_pos] == ' ') _pos++;
        }

        public YamlParseException Error(string message) => new(lineNumber, baseColumn + _pos, message);

        public DataNode ParseValue(int depth)
        {
            if (depth > MaxDepth)
                throw Error($"Document is nested more than {MaxDepth} levels deep.");
            SkipWs();
            if (AtEnd) throw Error("Unexpected end of value.");
            char c = text[_pos];
            if (c == '{') return ParseObject(depth);
            if (c == '[') return ParseArray(depth);
            if (c == '"') return new ScalarNode(InputSanitizer.Unescape(ScanDelimited('"')), ScalarKind.String);
            if (c == '\'') return new ScalarNode(ScanDelimited('\'').Replace("''", "'"), ScalarKind.String);
            return ParsePlainToken();
        }

        private DataNode ParseObject(int depth)
        {
            _pos++; // '{'
            var members = new List<(string, DataNode)>();
            SkipWs();
            if (!AtEnd && text[_pos] == '}') { _pos++; return new ObjectNode(members); }
            while (true)
            {
                SkipWs();
                string key = ParseKey();
                SkipWs();
                if (AtEnd || text[_pos] != ':')
                    throw Error("Expected ':' in mapping.");
                _pos++;
                var value = ParseValue(depth + 1);
                members.Add((key, value));
                SkipWs();
                if (AtEnd) throw Error("Unterminated '{'.");
                if (text[_pos] == ',') { _pos++; continue; }
                if (text[_pos] == '}') { _pos++; break; }
                throw Error("Expected ',' or '}'.");
            }
            return new ObjectNode(members);
        }

        private DataNode ParseArray(int depth)
        {
            _pos++; // '['
            var items = new List<DataNode>();
            SkipWs();
            if (!AtEnd && text[_pos] == ']') { _pos++; return new ArrayNode(items); }
            while (true)
            {
                items.Add(ParseValue(depth + 1));
                SkipWs();
                if (AtEnd) throw Error("Unterminated '['.");
                if (text[_pos] == ',') { _pos++; continue; }
                if (text[_pos] == ']') { _pos++; break; }
                throw Error("Expected ',' or ']'.");
            }
            return new ArrayNode(items);
        }

        private string ParseKey()
        {
            if (!AtEnd && text[_pos] == '"') return InputSanitizer.Unescape(ScanDelimited('"'));
            if (!AtEnd && text[_pos] == '\'') return ScanDelimited('\'').Replace("''", "'");
            int start = _pos;
            while (!AtEnd && text[_pos] is not (':' or ',' or '}' or ']'))
                _pos++;
            return text[start.._pos].TrimEnd();
        }

        private string ScanDelimited(char quote)
        {
            int start = ++_pos; // skip opening quote
            while (!AtEnd)
            {
                if (quote == '"' && text[_pos] == '\\' && _pos + 1 < text.Length) { _pos += 2; continue; }
                if (text[_pos] == quote)
                {
                    if (quote == '\'' && _pos + 1 < text.Length && text[_pos + 1] == '\'') { _pos += 2; continue; }
                    string inner = text[start.._pos];
                    _pos++; // closing quote
                    return inner;
                }
                _pos++;
            }
            throw Error("Unterminated quoted string.");
        }

        private DataNode ParsePlainToken()
        {
            int start = _pos;
            while (!AtEnd && text[_pos] is not (',' or '{' or '}' or '[' or ']' or ':'))
                _pos++;
            string token = text[start.._pos].TrimEnd();
            if (token.Length == 0)
                throw Error("Expected a value.");
            return ParsePlainScalar(token);
        }
    }

    // ---- Writing ---------------------------------------------------------------------------

    private static string Write(DataNode node, FormatOptions options)
    {
        int width = Math.Clamp(options.IndentWidth <= 0 ? 2 : options.IndentWidth, 1, 8);
        var sb = new StringBuilder();
        WriteBlock(sb, node, 0, width);
        return sb.ToString();
    }

    private static void WriteBlock(StringBuilder sb, DataNode node, int indent, int width)
    {
        switch (node)
        {
            case ObjectNode { Members.Count: 0 }:
                sb.Append(' ', indent).Append("{}\n");
                break;
            case ObjectNode obj:
                foreach (var (key, value) in obj.Members)
                    WriteMappingEntry(sb, key, value, indent, width);
                break;
            case ArrayNode { Items.Count: 0 }:
                sb.Append(' ', indent).Append("[]\n");
                break;
            case ArrayNode arr:
                foreach (var item in arr.Items)
                    WriteSequenceItem(sb, item, indent, width);
                break;
            case ScalarNode s:
                sb.Append(' ', indent).Append(ScalarLiteral(s)).Append('\n');
                break;
        }
    }

    private static void WriteMappingEntry(StringBuilder sb, string key, DataNode value, int indent, int width)
    {
        sb.Append(' ', indent).Append(QuoteKeyIfNeeded(key));
        if (IsInlineValue(value))
        {
            sb.Append(": ").Append(InlineLiteral(value)).Append('\n');
        }
        else
        {
            sb.Append(":\n");
            WriteBlock(sb, value, indent + width, width);
        }
    }

    /// <summary>
    /// Sequence items are indented under their key by <paramref name="width"/> (the more
    /// widely-taught "always indent sequences" convention, matching this app's own uniform
    /// nested-level indent for JSON/XML rather than the "dash at parent indent" style some YAML
    /// dumpers use). A sequence item that is itself a non-empty mapping inlines its first key
    /// right after the dash and aligns the rest two columns in — the width of <c>"- "</c> — which
    /// is the near-universal convention for that shape.
    /// </summary>
    private static void WriteSequenceItem(StringBuilder sb, DataNode item, int indent, int width)
    {
        if (IsInlineValue(item))
        {
            sb.Append(' ', indent).Append("- ").Append(InlineLiteral(item)).Append('\n');
            return;
        }

        if (item is ObjectNode obj)
        {
            int childIndent = indent + 2;
            bool first = true;
            foreach (var (key, value) in obj.Members)
            {
                sb.Append(' ', first ? indent : childIndent);
                if (first) { sb.Append("- "); first = false; }
                sb.Append(QuoteKeyIfNeeded(key));
                if (IsInlineValue(value))
                {
                    sb.Append(": ").Append(InlineLiteral(value)).Append('\n');
                }
                else
                {
                    sb.Append(":\n");
                    WriteBlock(sb, value, childIndent + width, width);
                }
            }
            return;
        }

        // A non-empty ArrayNode nested directly inside a sequence item (a list of lists).
        sb.Append(' ', indent).Append("-\n");
        WriteBlock(sb, item, indent + width, width);
    }

    private static bool IsInlineValue(DataNode node) =>
        node is ScalarNode or ObjectNode { Members.Count: 0 } or ArrayNode { Items.Count: 0 };

    private static string InlineLiteral(DataNode node) => node switch
    {
        ScalarNode s => ScalarLiteral(s),
        ObjectNode => "{}",
        ArrayNode => "[]",
        _ => throw new InvalidOperationException("Not an inline value."),
    };

    private static string ScalarLiteral(ScalarNode s) => s.Kind switch
    {
        ScalarKind.Null => "null",
        ScalarKind.Boolean => s.Raw == "true" ? "true" : "false",
        ScalarKind.Number => string.IsNullOrEmpty(s.Raw) ? "0" : s.Raw,
        _ => QuoteStringIfNeeded(s.Raw ?? ""),
    };

    private static string QuoteKeyIfNeeded(string key) => NeedsQuoting(key) ? QuoteDouble(key) : key;
    private static string QuoteStringIfNeeded(string value) => NeedsQuoting(value) ? QuoteDouble(value) : value;

    /// <summary>
    /// Whether a plain (unquoted) scalar would be unsafe or ambiguous — errs toward quoting too
    /// often rather than too rarely, since over-quoting is only a cosmetic cost while under-quoting
    /// can silently change a round-tripped string into a bool/null/number on the next parse.
    /// </summary>
    private static bool NeedsQuoting(string value)
    {
        if (value.Length == 0) return true;
        if (value != value.Trim()) return true;
        if (value.AsSpan().IndexOfAny('\n', '\r', '\t') >= 0) return true;
        char first = value[0];
        if ("!&*?|>%@`\"'#,[]{}:- ".IndexOf(first) >= 0) return true;
        if (value.Contains(": ", StringComparison.Ordinal)) return true;
        if (value.EndsWith(':')) return true;
        if (value.Contains(" #", StringComparison.Ordinal)) return true;
        if (value is "true" or "false" or "null" or "~") return true;
        return IsNumberLiteral(value);
    }

    private static string QuoteDouble(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}

/// <summary>One YAML parse failure, at the (1-based) line/column where it was noticed — the same
/// shape <see cref="System.Text.Json.JsonException"/> and <see cref="System.Xml.XmlException"/>
/// already give <see cref="JsonFormat"/> and <see cref="XmlFormat"/>.</summary>
internal sealed class YamlParseException(int line, int column, string message) : Exception(message)
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}
