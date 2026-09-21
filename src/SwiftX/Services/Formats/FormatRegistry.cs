using SwiftX.Models;
using SwiftX.Services.Syntax;

namespace SwiftX.Services.Formats;

/// <summary>JSON, XML, YAML and CSV format implementations with Auto-detect.</summary>
public static class FormatRegistry
{
    public static readonly IDataFormat Json = JsonFormat.Instance;
    public static readonly IDataFormat Xml = XmlFormat.Instance;
    public static readonly IDataFormat Yaml = YamlFormat.Instance;
    public static readonly IDataFormat Csv = CsvFormat.Instance;

    // Json and Xml lead the list so DetectBest's "strictly greater score wins" tie-break favors
    // them over Yaml/Csv on genuinely ambiguous input — see each format's own DetectConfidence,
    // which is written to score 0 rather than compete whenever the input already looks like JSON
    // or XML (a brace/bracket/angle-bracket lead character).
    public static readonly IReadOnlyList<IDataFormat> All = [Json, Xml, Yaml, Csv];

    public static readonly IDataFormat Auto = new AutoDataFormat();

    public static IDataFormat DetectBest(string text) => Detect(text).Format;

    /// <summary>
    /// The best-scoring format <em>and the score it got</em>, in one pass.
    /// <para>
    /// A caller that has to know both — "which format is this, and was it recognizable as one at
    /// all?" — otherwise runs the whole detection twice: <see cref="DetectBest"/> scores all four
    /// formats and throws the numbers away, and the caller then calls
    /// <see cref="IDataFormat.DetectConfidence"/> again on the winner. Two of the four scorers
    /// (<see cref="YamlFormat"/> and <see cref="CsvFormat"/>) walk every line of the document to
    /// answer, so the second pass is a full extra scan of the user's paste on the UI thread for a
    /// number that was already computed a moment earlier.
    /// </para>
    /// </summary>
    public static (IDataFormat Format, int Confidence) Detect(string text)
    {
        IDataFormat best = Json;
        int bestScore = 0;
        foreach (var format in All)
        {
            int score = format.DetectConfidence(text);
            if (score > bestScore)
            {
                bestScore = score;
                best = format;
            }
        }
        return (best, bestScore);
    }

    private sealed class AutoDataFormat : IDataFormat
    {
        public string Id => "auto";
        public string DisplayNameKey => "Format.Auto";
        public string[] Extensions => [".json", ".xml", ".yaml", ".yml", ".csv"];
        public ITokenizer Tokenizer => PlainTokenizer.Instance;
        public int DetectConfidence(ReadOnlySpan<char> text) => 0;
        public FormatResult Format(string text, FormatOptions options) => DetectBest(text).Format(text, options);
        public FormatResult Minify(string text) => DetectBest(text).Minify(text);
        public IReadOnlyList<Diagnostic> Validate(string text) => DetectBest(text).Validate(text);
        public DataNode ToCanonical(string text) => DetectBest(text).ToCanonical(text);
        public string FromCanonical(DataNode node, FormatOptions options) => Json.FromCanonical(node, options);
    }
}
