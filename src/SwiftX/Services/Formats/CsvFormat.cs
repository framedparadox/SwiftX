using System.Text;
using SwiftX.Models;
using SwiftX.Services.Syntax;

namespace SwiftX.Services.Formats;

/// <summary>
/// RFC 4180 CSV: comma-separated fields, <c>"..."</c> quoting with <c>""</c> as an escaped quote,
/// and rows separated by <c>\n</c> or <c>\r\n</c>. A ragged row (more or fewer fields than its
/// neighbours) is legal CSV and left alone — the one thing this format actually rejects is an
/// unterminated quoted field, since that is the one shape a text editor's Format/Validate can't
/// make sense of at all.
/// <para>
/// No syntax highlighting (<see cref="Tokenizer"/> is the no-op <see cref="PlainTokenizer"/>):
/// columns aren't "syntax" the way JSON/XML/YAML keys and punctuation are, so there is nothing a
/// tokenizer would usefully color here.
/// </para>
/// </summary>
public sealed class CsvFormat : IDataFormat
{
    public static readonly CsvFormat Instance = new();

    public string Id => "csv";
    public string DisplayNameKey => "Format.Csv";
    public string[] Extensions => [".csv"];
    public ITokenizer Tokenizer => PlainTokenizer.Instance;

    public int DetectConfidence(ReadOnlySpan<char> text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return 0;
        char first = trimmed[0];
        if (first is '{' or '[' or '<') return 0;

        int lines = 0;
        int? firstCount = null;
        bool consistent = true;

        foreach (var lineSpan in trimmed.EnumerateLines())
        {
            if (lineSpan.IsWhiteSpace()) continue;
            lines++;
            int commas = CountTopLevelCommas(lineSpan);
            if (commas == 0) { consistent = false; continue; }
            if (firstCount is null) firstCount = commas;
            else if (commas != firstCount) consistent = false;
        }

        // A single line with a comma in it is indistinguishable from an ordinary sentence ("Hello,
        // world") — CSV's defining feature is tabular *rows*, so this format only ever claims
        // multi-line input, however many commas one line happens to contain.
        if (lines < 2 || firstCount is null) return 0;
        return consistent ? 65 : 25;
    }

    private static int CountTopLevelCommas(ReadOnlySpan<char> line)
    {
        int count = 0;
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ',' && !inQuotes) count++;
        }
        return count;
    }

    public FormatResult Format(string text, FormatOptions options)
    {
        var rows = ParseRows(text, out var error);
        return error is not null ? FormatResult.Fail(error) : FormatResult.Ok(Write(rows));
    }

    /// <summary>CSV has no separate compact form distinct from its own normalized shape (unlike
    /// JSON/XML, there is no extra whitespace to strip), so Minify and Format coincide.</summary>
    public FormatResult Minify(string text) => Format(text, FormatOptions.Default);

    public IReadOnlyList<Diagnostic> Validate(string text)
    {
        ParseRows(text, out var error);
        return error is null ? [] : [error];
    }

    /// <summary>
    /// The first row becomes object keys (de-duplicated as <c>name_2</c>, <c>name_3</c>, ...);
    /// every later row becomes one object, missing trailing cells filling in as empty strings and
    /// any extra cells past the header count landing under synthetic <c>_extra1</c>, <c>_extra2</c>,
    /// ... keys — this format never throws on a ragged table, only on the unterminated-quote case
    /// <see cref="ParseRows"/> itself reports.
    /// </summary>
    public DataNode ToCanonical(string text)
    {
        var rows = ParseRows(text, out var error);
        if (error is not null)
            throw new InvalidOperationException(error.Message);
        if (rows.Count == 0)
            return new ArrayNode([]);

        var headers = MakeUniqueHeaders(rows[0]);
        var items = new List<DataNode>();
        for (int r = 1; r < rows.Count; r++)
        {
            var cells = rows[r];
            var members = new List<(string, DataNode)>();
            for (int c = 0; c < headers.Count; c++)
                members.Add((headers[c], new ScalarNode(c < cells.Count ? cells[c] : "", ScalarKind.String)));
            for (int c = headers.Count; c < cells.Count; c++)
                members.Add(($"_extra{c - headers.Count + 1}", new ScalarNode(cells[c], ScalarKind.String)));
            items.Add(new ObjectNode(members));
        }
        return new ArrayNode(items);
    }

    /// <summary>
    /// The inverse of <see cref="ToCanonical"/>'s common shape (an array of flat objects becomes a
    /// header row plus one row per object, the header the union of every object's keys in
    /// first-seen order) with pragmatic fallbacks for anything else a canonical tree could be — a
    /// plain array becomes one row, a single object becomes a two-row key/value table, and a bare
    /// scalar becomes a single cell. A nested object/array value that lands in a cell is flattened
    /// to compact JSON text rather than dropped.
    /// </summary>
    public string FromCanonical(DataNode node, FormatOptions options) => Write(BuildRows(node));

    private static List<List<string>> BuildRows(DataNode node)
    {
        if (node is ArrayNode arr)
        {
            if (arr.Items.Count == 0)
                return [];
            if (arr.Items.All(i => i is ObjectNode))
            {
                var headers = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in arr.Items)
                    foreach (var (key, _) in ((ObjectNode)item).Members)
                        if (seen.Add(key)) headers.Add(key);

                var rows = new List<List<string>> { headers };
                foreach (var item in arr.Items)
                {
                    var obj = (ObjectNode)item;
                    var row = new List<string>(headers.Count);
                    foreach (var header in headers)
                    {
                        var match = obj.Members.FirstOrDefault(m => m.Key == header);
                        row.Add(CellText(match.Value));
                    }
                    rows.Add(row);
                }
                return rows;
            }
            return [arr.Items.Select(CellText).ToList()];
        }

        if (node is ObjectNode obj2)
            return [obj2.Members.Select(m => m.Key).ToList(), obj2.Members.Select(m => CellText(m.Value)).ToList()];

        return [[CellText(node)]];
    }

    private static string CellText(DataNode? node) => node switch
    {
        null => "",
        ScalarNode { Kind: ScalarKind.Null } => "",
        ScalarNode s => s.Raw ?? "",
        _ => JsonFormat.Instance.Minify(JsonFormat.Instance.FromCanonical(node, FormatOptions.Default)).Text,
    };

    private static List<string> MakeUniqueHeaders(List<string> firstRow)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>(firstRow.Count);
        foreach (var raw in firstRow)
        {
            string name = string.IsNullOrEmpty(raw) ? "column" : raw;
            if (seen.TryGetValue(name, out int n))
            {
                n++;
                seen[name] = n;
                name = $"{name}_{n}";
            }
            else
            {
                seen[name] = 1;
            }
            result.Add(name);
        }
        return result;
    }

    private static string Write(List<List<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (int i = 0; i < row.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(EncodeField(row[i]));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static readonly char[] SpecialChars = [',', '"', '\n', '\r'];

    private static string EncodeField(string field) =>
        field.IndexOfAny(SpecialChars) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;

    /// <summary>
    /// RFC 4180 field/row scanner. A quote only opens a quoted field when it is the field's very
    /// first character (per the spec); anywhere else it is a literal character, which is the
    /// lenient reading real-world CSV needs. The only failure this reports is an unterminated
    /// quoted field — everything else (ragged rows, empty fields, empty lines as a one-empty-field
    /// row) is legal CSV and passed through as-is.
    /// </summary>
    private static List<List<string>> ParseRows(string text, out Diagnostic? error)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int line = 1, col = 1;

        void EndField() { row.Add(field.ToString()); field.Clear(); }
        void EndRow() { EndField(); rows.Add(row); row = []; }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; col += 2; continue; }
                    inQuotes = false; i++; col++; continue;
                }
                if (c == '\n') { field.Append('\n'); i++; line++; col = 1; continue; }
                if (c == '\r') { i++; continue; }
                field.Append(c); i++; col++; continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true; i++; col++; continue;
                case ',':
                    EndField(); i++; col++; continue;
                case '\r':
                    i++; continue;
                case '\n':
                    EndRow(); i++; line++; col = 1; continue;
                default:
                    field.Append(c); i++; col++; continue;
            }
        }

        if (inQuotes)
        {
            error = new Diagnostic(line, col, "Unterminated quoted field.");
            return rows;
        }

        if (field.Length > 0 || row.Count > 0)
            EndRow();

        error = null;
        return rows;
    }
}
