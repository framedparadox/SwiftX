using System.Text;
using SwiftX.Models;
using SwiftX.Services.Syntax;

namespace SwiftX.Services.Formats;

/// <summary>
/// CSV format: RFC 4180-ish parse/rewrite. Format normalizes quoting and line endings;
/// minify collapses to a compact single-line-per-record form without extra spaces.
/// </summary>
public sealed class CsvFormat : IDataFormat
{
    public static readonly CsvFormat Instance = new();

    public string Id => "csv";
    public string DisplayNameKey => "Format.Csv";
    public string[] Extensions => [".csv"];
    public ITokenizer Tokenizer => CsvTokenizer.Instance;

    public int DetectConfidence(ReadOnlySpan<char> text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return 0;

        char first = trimmed[0];
        if (first is '{' or '[' or '<')
            return 0;

        int commas = 0, semicolons = 0, tabs = 0, newlines = 0;
        bool inQuotes = false;
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < trimmed.Length && trimmed[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                inQuotes = !inQuotes;
                continue;
            }
            if (inQuotes)
                continue;
            switch (c)
            {
                case ',': commas++; break;
                case ';': semicolons++; break;
                case '\t': tabs++; break;
                case '\n': newlines++; break;
            }
        }

        int separators = Math.Max(commas, Math.Max(semicolons, tabs));
        if (separators == 0)
            return 0;

        // Prefer multi-row tabular data; a single comma-separated line is still a weak CSV signal.
        if (newlines > 0 && separators >= newlines)
            return 70;
        if (separators >= 2)
            return 45;
        return 25;
    }

    public FormatResult Format(string text, FormatOptions options)
    {
        try
        {
            var rows = Parse(text);
            return FormatResult.Ok(Write(rows, compact: false));
        }
        catch (FormatException ex)
        {
            return FormatResult.Fail(new Diagnostic(1, 1, ex.Message));
        }
    }

    public FormatResult Minify(string text)
    {
        try
        {
            var rows = Parse(text);
            return FormatResult.Ok(Write(rows, compact: true));
        }
        catch (FormatException ex)
        {
            return FormatResult.Fail(new Diagnostic(1, 1, ex.Message));
        }
    }

    public IReadOnlyList<Diagnostic> Validate(string text)
    {
        try
        {
            Parse(text);
            return [];
        }
        catch (FormatException ex)
        {
            return [new Diagnostic(1, 1, ex.Message)];
        }
    }

    public DataNode ToCanonical(string text)
    {
        var rows = Parse(text);
        if (rows.Count == 0)
            return new ArrayNode([]);

        // Header row → array of objects when every subsequent row matches the header width.
        var header = rows[0];
        bool asObjects = rows.Count > 1 && rows.Skip(1).All(r => r.Count == header.Count)
                         && header.All(h => !string.IsNullOrWhiteSpace(h));
        if (asObjects)
        {
            var objects = new List<DataNode>();
            foreach (var row in rows.Skip(1))
            {
                var members = new List<(string Key, DataNode Value)>();
                for (int i = 0; i < header.Count; i++)
                    members.Add((header[i], new ScalarNode(row[i], ScalarKind.String)));
                objects.Add(new ObjectNode(members));
            }
            return new ArrayNode(objects);
        }

        return new ArrayNode(rows.Select(r =>
            (DataNode)new ArrayNode(r.Select(c => (DataNode)new ScalarNode(c, ScalarKind.String)).ToList())).ToList());
    }

    public string FromCanonical(DataNode node, FormatOptions options)
    {
        var rows = new List<IReadOnlyList<string>>();
        switch (node)
        {
            case ArrayNode { Items: [ObjectNode first, ..] } arr when arr.Items.All(i => i is ObjectNode):
                var keys = first.Members.Select(m => m.Key).ToList();
                rows.Add(keys);
                foreach (var item in arr.Items.Cast<ObjectNode>())
                {
                    var map = item.Members.ToDictionary(m => m.Key, m => ScalarText(m.Value));
                    rows.Add(keys.Select(k => map.TryGetValue(k, out var v) ? v : "").ToList());
                }
                break;
            case ArrayNode arr:
                foreach (var item in arr.Items)
                {
                    if (item is ArrayNode inner)
                        rows.Add(inner.Items.Select(ScalarText).ToList());
                    else
                        rows.Add([ScalarText(item)]);
                }
                break;
            default:
                rows.Add([ScalarText(node)]);
                break;
        }
        return Write(rows, compact: false);
    }

    // ---- Internals ----------------------------------------------------------------------

    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                case ';':
                case '\t':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (inQuotes)
            throw new FormatException("Unterminated quoted field.");

        // Trailing content or a final record without a newline.
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        // Drop a single trailing empty row produced by a final newline.
        if (rows.Count > 0 && rows[^1].Count == 1 && rows[^1][0].Length == 0)
            rows.RemoveAt(rows.Count - 1);

        return rows;
    }

    private static string Write(IReadOnlyList<IReadOnlyList<string>> rows, bool compact)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows.Count; r++)
        {
            if (r > 0)
                sb.Append('\n');
            var row = rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0)
                    sb.Append(compact ? ',' : ", ");
                sb.Append(EscapeField(row[c], forceQuote: !compact && NeedsQuote(row[c])));
            }
        }
        return sb.ToString();
    }

    private static bool NeedsQuote(string field) =>
        field.IndexOfAny([',', ';', '\t', '"', '\n', '\r']) >= 0
        || field.StartsWith(' ')
        || field.EndsWith(' ');

    private static string EscapeField(string field, bool forceQuote)
    {
        bool quote = forceQuote || NeedsQuote(field);
        if (!quote)
            return field;
        return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string ScalarText(DataNode node) => node switch
    {
        ScalarNode s => s.Raw ?? "",
        _ => node.ToString() ?? "",
    };
}
