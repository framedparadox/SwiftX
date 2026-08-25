namespace SwiftX.Services.Syntax;

/// <summary>Lightweight CSV highlighter: commas/newlines as punctuation, quoted fields as strings.</summary>
public sealed class CsvTokenizer : ITokenizer
{
    public static readonly CsvTokenizer Instance = new();

    public IReadOnlyList<Token> Tokenize(string text)
    {
        if (text.Length == 0)
            return [];

        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is ',' or ';' or '\t' or '\n' or '\r')
            {
                int start = i++;
                if (c == '\r' && i < text.Length && text[i] == '\n')
                    i++;
                tokens.Add(new Token(start, i - start, TokenKind.Punctuation));
                continue;
            }

            if (c == '"')
            {
                int start = i++;
                while (i < text.Length)
                {
                    if (text[i] == '"')
                    {
                        i++;
                        if (i < text.Length && text[i] == '"')
                        {
                            i++; // escaped quote
                            continue;
                        }
                        break;
                    }
                    i++;
                }
                tokens.Add(new Token(start, i - start, TokenKind.String));
                continue;
            }

            int plainStart = i++;
            while (i < text.Length && text[i] is not (',' or ';' or '\t' or '\n' or '\r' or '"'))
                i++;
            tokens.Add(new Token(plainStart, i - plainStart, TokenKind.Plain));
        }
        return tokens;
    }

    public IReadOnlyList<Token> TokenizeRange(string text, LineIndex lines, int first, int last, ScannerState entryState) =>
        TokenizerRangeSupport.TokenizeRangeViaWholeDocument(this, text, lines, first, last);

    public ScannerState StateAfter(string text, LineIndex lines, int line, ScannerState entryState) => ScannerState.Initial;
}
