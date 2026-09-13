namespace SwiftX.Services.Syntax;

/// <summary>
/// Hand-written single-pass YAML scanner, line-oriented since YAML's structure is
/// indentation-driven rather than bracket-driven. Covers the common surface — comments, block
/// mapping keys, quoted strings, booleans/null/numbers, sequence dashes, and flow-collection
/// punctuation — and leaves everything else (plain scalars, anchors, tags) as an untokenized gap
/// that <c>CodeView</c> renders as <see cref="TokenKind.Plain"/>. Never throws on malformed input.
/// <para>
/// Deliberately approximate in one place: a <c>#</c> encountered while scanning a flow value's
/// plain scalar (e.g. a URL fragment like <c>http://x#y</c>) is always read as a comment start
/// here, even mid-token — real YAML only treats it as one when preceded by whitespace. Getting
/// that right needs it to see back through the trimming this tokenizer skips, which cannot change
/// the underlying data (only <see cref="Formats.YamlFormat"/>'s own inline-comment stripping does,
/// and that one <em>does</em> require the preceding-whitespace rule), so the gap here is purely
/// cosmetic — an occasional over-eager comment color, never a change to what Format/Validate see.
/// </para>
/// </summary>
public sealed class YamlTokenizer : ITokenizer
{
    public static readonly YamlTokenizer Instance = new();

    public IReadOnlyList<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0;
        int n = text.Length;
        while (i < n)
        {
            int lineEnd = text.IndexOf('\n', i);
            if (lineEnd < 0) lineEnd = n;
            int contentEnd = lineEnd > i && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            TokenizeLine(text, i, contentEnd, tokens);
            i = lineEnd + 1;
        }
        return tokens;
    }

    private static void TokenizeLine(string text, int start, int end, List<Token> tokens)
    {
        int i = start;
        while (i < end && text[i] == ' ') i++;
        if (i >= end) return;

        if (text[i] == '#')
        {
            tokens.Add(new Token(i, end - i, TokenKind.Comment));
            return;
        }

        if (StartsWithBoundary(text, i, end, "---") || StartsWithBoundary(text, i, end, "..."))
        {
            tokens.Add(new Token(i, 3, TokenKind.Punctuation));
            i += 3;
            while (i < end && text[i] == ' ') i++;
            if (i >= end) return;
            if (text[i] == '#') { tokens.Add(new Token(i, end - i, TokenKind.Comment)); return; }
        }

        // One or more sequence dashes ("- - value" for a nested inline sequence).
        while (i < end && text[i] == '-' && (i + 1 == end || text[i + 1] == ' '))
        {
            tokens.Add(new Token(i, 1, TokenKind.Punctuation));
            i++;
            while (i < end && text[i] == ' ') i++;
            if (i >= end) return;
            if (text[i] == '#') { tokens.Add(new Token(i, end - i, TokenKind.Comment)); return; }
        }

        TokenizeContent(text, i, end, tokens);
    }

    private static void TokenizeContent(string text, int i, int end, List<Token> tokens)
    {
        if (i >= end) return;
        if (text[i] == '#') { tokens.Add(new Token(i, end - i, TokenKind.Comment)); return; }

        int colon = FindTopLevelColon(text, i, end);
        if (colon >= 0)
        {
            TokenizeScalar(text, i, colon, tokens, isKey: true);
            tokens.Add(new Token(colon, 1, TokenKind.Punctuation));
            int j = colon + 1;
            while (j < end && text[j] == ' ') j++;
            if (j >= end) return;
            if (text[j] == '#') { tokens.Add(new Token(j, end - j, TokenKind.Comment)); return; }
            TokenizeFlow(text, j, end, tokens);
            return;
        }

        TokenizeFlow(text, i, end, tokens);
    }

    /// <summary>Punctuation for flow-collection brackets/commas/colons and any quoted strings or
    /// recognizable scalars inside a value span; a plain scalar in between is left as a gap.</summary>
    private static void TokenizeFlow(string text, int i, int end, List<Token> tokens)
    {
        while (i < end)
        {
            char c = text[i];
            if (c == ' ') { i++; continue; }
            if (c is '{' or '}' or '[' or ']' or ',' or ':')
            {
                tokens.Add(new Token(i, 1, TokenKind.Punctuation));
                i++;
                continue;
            }
            if (c == '"')
            {
                int qs = i;
                i = ScanDoubleQuoted(text, i, end);
                tokens.Add(new Token(qs, i - qs, TokenKind.String));
                continue;
            }
            if (c == '\'')
            {
                int qs = i;
                i = ScanSingleQuoted(text, i, end);
                tokens.Add(new Token(qs, i - qs, TokenKind.String));
                continue;
            }
            if (c == '#')
            {
                tokens.Add(new Token(i, end - i, TokenKind.Comment));
                return;
            }

            int scalarStart = i;
            while (i < end && text[i] is not (' ' or ',' or '{' or '}' or '[' or ']' or ':' or '#' or '"' or '\''))
                i++;
            if (i > scalarStart)
                TokenizeScalar(text, scalarStart, i, tokens, isKey: false);
            else
                i++; // stuck on something unexpected; guarantee forward progress
        }
    }

    private static void TokenizeScalar(string text, int start, int end, List<Token> tokens, bool isKey)
    {
        while (start < end && text[start] == ' ') start++;
        while (end > start && text[end - 1] == ' ') end--;
        if (start >= end) return;

        if ((text[start] == '"' || text[start] == '\'') && text[end - 1] == text[start] && end - start >= 2)
        {
            tokens.Add(new Token(start, end - start, isKey ? TokenKind.PropertyName : TokenKind.String));
            return;
        }
        if (isKey)
        {
            tokens.Add(new Token(start, end - start, TokenKind.PropertyName));
            return;
        }

        var span = text.AsSpan(start, end - start);
        if (span.SequenceEqual("true") || span.SequenceEqual("false"))
        {
            tokens.Add(new Token(start, end - start, TokenKind.Boolean));
            return;
        }
        if (span.SequenceEqual("null") || span.SequenceEqual("~"))
        {
            tokens.Add(new Token(start, end - start, TokenKind.Null));
            return;
        }
        if (IsNumber(span))
            tokens.Add(new Token(start, end - start, TokenKind.Number));
        // Otherwise a plain scalar: left untokenized (renders as Plain).
    }

    private static bool IsNumber(ReadOnlySpan<char> span)
    {
        int i = 0;
        if (i < span.Length && (span[i] == '-' || span[i] == '+')) i++;
        int digitsStart = i;
        while (i < span.Length && char.IsAsciiDigit(span[i])) i++;
        if (i == digitsStart) return false;
        if (i < span.Length && span[i] == '.')
        {
            i++;
            int fracStart = i;
            while (i < span.Length && char.IsAsciiDigit(span[i])) i++;
            if (i == fracStart) return false;
        }
        if (i < span.Length && (span[i] == 'e' || span[i] == 'E'))
        {
            i++;
            if (i < span.Length && (span[i] == '+' || span[i] == '-')) i++;
            int expStart = i;
            while (i < span.Length && char.IsAsciiDigit(span[i])) i++;
            if (i == expStart) return false;
        }
        return i == span.Length;
    }

    private static int FindTopLevelColon(string text, int start, int end)
    {
        bool inSingle = false, inDouble = false;
        int depth = 0;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (inDouble)
            {
                if (c == '\\' && i + 1 < end) { i++; continue; }
                if (c == '"') inDouble = false;
                continue;
            }
            if (inSingle)
            {
                if (c == '\'')
                {
                    if (i + 1 < end && text[i + 1] == '\'') { i++; continue; }
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
                case ':' when depth == 0 && (i + 1 == end || text[i + 1] == ' '):
                    return i;
            }
        }
        return -1;
    }

    private static int ScanDoubleQuoted(string text, int start, int end)
    {
        int i = start + 1;
        while (i < end)
        {
            if (text[i] == '\\' && i + 1 < end) { i += 2; continue; }
            if (text[i] == '"') return i + 1;
            i++;
        }
        return end; // unterminated: consume to end rather than throw
    }

    private static int ScanSingleQuoted(string text, int start, int end)
    {
        int i = start + 1;
        while (i < end)
        {
            if (text[i] == '\'')
            {
                if (i + 1 < end && text[i + 1] == '\'') { i += 2; continue; }
                return i + 1;
            }
            i++;
        }
        return end;
    }

    private static bool StartsWithBoundary(string text, int i, int end, string token) =>
        i + token.Length <= end && string.CompareOrdinal(text, i, token, 0, token.Length) == 0 &&
        (i + token.Length == end || text[i + token.Length] == ' ');

    public IReadOnlyList<Token> TokenizeRange(string text, LineIndex lines, int first, int last, ScannerState entryState) =>
        TokenizerRangeSupport.TokenizeRangeViaWholeDocument(this, text, lines, first, last);

    public ScannerState StateAfter(string text, LineIndex lines, int line, ScannerState entryState) => ScannerState.Initial;
}
