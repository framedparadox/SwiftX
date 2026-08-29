using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SwiftX.Models;
using SwiftX.Services.Syntax;

namespace SwiftX.Services.Formats;

/// <summary>
/// The JSON format, backed by <see cref="System.Text.Json"/> exclusively (design doc §14.1).
/// Numbers are round-tripped by raw lexeme (<see cref="JsonElement.GetRawText"/>) rather than
/// through <c>double</c>, so a 20-digit id or a high-precision decimal survives Format/Minify and
/// the <see cref="DataNode"/> round trip exactly.
/// </summary>
public sealed class JsonFormat : IDataFormat
{
    public static readonly JsonFormat Instance = new();

    public string Id => "json";
    public string DisplayNameKey => "Format.Json";
    public string[] Extensions => [".json"];
    public ITokenizer Tokenizer => JsonTokenizer.Instance;

    public int DetectConfidence(ReadOnlySpan<char> text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return 0;
        char first = trimmed[0];
        char last = trimmed[^1];
        bool bracesMatch = (first == '{' && last == '}') || (first == '[' && last == ']');
        if (!bracesMatch)
            return first is '"' && last is '"' ? 20 : 0;
        return 80;
    }

    public FormatResult Format(string text, FormatOptions options)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, ParseOptions);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, WriterOptions(indented: true, options.IndentWidth)))
                WriteElement(writer, doc.RootElement, options.SortKeys);
            return FormatResult.Ok(Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (JsonException ex)
        {
            return FormatResult.Fail(ToDiagnostic(text, ex));
        }
    }

    public FormatResult Minify(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, ParseOptions);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, WriterOptions(indented: false, 0)))
                WriteElement(writer, doc.RootElement, sortKeys: false);
            return FormatResult.Ok(Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (JsonException ex)
        {
            return FormatResult.Fail(ToDiagnostic(text, ex));
        }
    }

    public IReadOnlyList<Diagnostic> Validate(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, ParseOptions);
            return [];
        }
        catch (JsonException ex)
        {
            return [ToDiagnostic(text, ex)];
        }
    }

    public DataNode ToCanonical(string text)
    {
        using var doc = JsonDocument.Parse(text, ParseOptions);
        return Convert(doc.RootElement);
    }

    public string FromCanonical(DataNode node, FormatOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions(indented: true, options.IndentWidth)))
            WriteNode(writer, node, options.SortKeys);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // ---- Internals ----------------------------------------------------------------------

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 256,
    };

    private static JsonWriterOptions WriterOptions(bool indented, int indentWidth) => new()
    {
        Indented = indented,
        IndentSize = Math.Clamp(indentWidth <= 0 ? 2 : indentWidth, 1, 127),
        SkipValidation = true, // this writer only ever mirrors an already-valid document/tree
        // The default encoder escapes every non-ASCII character plus a handful of ASCII
        // punctuation (<, >, &, ', +) as \uXXXX — a web-embedding safety default that has nothing
        // to do with a desktop formatter. Format/Minify are supposed to rewrite structure and
        // whitespace, not silently rewrite a user's café/emoji/HTML-snippet string content into
        // escape sequences it never had.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// <see cref="JsonException.BytePositionInLine"/> counts UTF-8 bytes, not UTF-16 characters —
    /// System.Text.Json parses by transcoding to UTF-8 internally. Any non-ASCII character earlier
    /// on the same line (an accented letter, an emoji, CJK text) makes that raw byte count run
    /// ahead of the character column a text editor — and this app's own <see cref="LineIndex"/> —
    /// would report, so it is walked back to a UTF-16 code-unit column here rather than surfaced
    /// as-is.
    /// </summary>
    private static Diagnostic ToDiagnostic(string text, JsonException ex)
    {
        int line = (int)(ex.LineNumber ?? 0);
        int byteColumn = (int)(ex.BytePositionInLine ?? 0);
        return new Diagnostic(line + 1, ByteOffsetToCharColumn(text, line, byteColumn), ex.Message);
    }

    private static int ByteOffsetToCharColumn(string text, int line, int byteOffset)
    {
        var lines = new LineIndex(text);
        int start = lines.StartOf(line);
        int end = line + 1 < lines.LineCount ? lines.StartOf(line + 1) : text.Length;

        int consumedBytes = 0;
        int i = start;
        while (i < end && consumedBytes < byteOffset)
        {
            int charLen = char.IsHighSurrogate(text[i]) && i + 1 < end && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            consumedBytes += Encoding.UTF8.GetByteCount(text.AsSpan(i, charLen));
            i += charLen;
        }
        return i - start + 1;
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element, bool sortKeys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var props = element.EnumerateObject();
                IEnumerable<JsonProperty> orderedProps = sortKeys
                    ? props.OrderBy(p => p.Name, StringComparer.Ordinal)
                    : props;
                foreach (var p in orderedProps)
                {
                    writer.WritePropertyName(p.Name);
                    WriteElement(writer, p.Value, sortKeys);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteElement(writer, item, sortKeys);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    private static DataNode Convert(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new ObjectNode(element.EnumerateObject()
            .Select(p => (p.Name, Convert(p.Value))).ToList()),
        JsonValueKind.Array => new ArrayNode(element.EnumerateArray().Select(Convert).ToList()),
        JsonValueKind.String => new ScalarNode(element.GetString(), ScalarKind.String),
        JsonValueKind.Number => new ScalarNode(element.GetRawText(), ScalarKind.Number),
        JsonValueKind.True => new ScalarNode("true", ScalarKind.Boolean),
        JsonValueKind.False => new ScalarNode("false", ScalarKind.Boolean),
        _ => new ScalarNode(null, ScalarKind.Null),
    };

    private static void WriteNode(Utf8JsonWriter writer, DataNode node, bool sortKeys)
    {
        switch (node)
        {
            case ObjectNode obj:
                writer.WriteStartObject();
                IEnumerable<(string Key, DataNode Value)> members = sortKeys
                    ? obj.Members.OrderBy(m => m.Key, StringComparer.Ordinal)
                    : obj.Members;
                foreach (var (key, value) in members)
                {
                    writer.WritePropertyName(key);
                    WriteNode(writer, value, sortKeys);
                }
                writer.WriteEndObject();
                break;
            case ArrayNode arr:
                writer.WriteStartArray();
                foreach (var item in arr.Items)
                    WriteNode(writer, item, sortKeys);
                writer.WriteEndArray();
                break;
            case ScalarNode scalar:
                switch (scalar.Kind)
                {
                    case ScalarKind.String:
                        writer.WriteStringValue(scalar.Raw ?? "");
                        break;
                    case ScalarKind.Number:
                        writer.WriteRawValue(string.IsNullOrEmpty(scalar.Raw) ? "0" : scalar.Raw, skipInputValidation: true);
                        break;
                    case ScalarKind.Boolean:
                        writer.WriteBooleanValue(scalar.Raw == "true");
                        break;
                    default:
                        writer.WriteNullValue();
                        break;
                }
                break;
        }
    }
}
