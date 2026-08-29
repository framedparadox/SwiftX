using SwiftX.Models;
using SwiftX.Services.Syntax;

namespace SwiftX.Services.Formats;

/// <summary>JSON and XML format implementations with Auto-detect.</summary>
public static class FormatRegistry
{
    public static readonly IDataFormat Json = JsonFormat.Instance;
    public static readonly IDataFormat Xml = XmlFormat.Instance;

    public static readonly IReadOnlyList<IDataFormat> All = [Json, Xml];

    public static readonly IDataFormat Auto = new AutoDataFormat();

    public static IDataFormat DetectBest(string text)
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
        return best;
    }

    private sealed class AutoDataFormat : IDataFormat
    {
        public string Id => "auto";
        public string DisplayNameKey => "Format.Auto";
        public string[] Extensions => [".json", ".xml"];
        public ITokenizer Tokenizer => PlainTokenizer.Instance;
        public int DetectConfidence(ReadOnlySpan<char> text) => 0;
        public FormatResult Format(string text, FormatOptions options) => DetectBest(text).Format(text, options);
        public FormatResult Minify(string text) => DetectBest(text).Minify(text);
        public IReadOnlyList<Diagnostic> Validate(string text) => DetectBest(text).Validate(text);
        public DataNode ToCanonical(string text) => DetectBest(text).ToCanonical(text);
        public string FromCanonical(DataNode node, FormatOptions options) => Json.FromCanonical(node, options);
    }
}
