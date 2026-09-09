namespace SwiftX.Services.Syntax;

/// <summary>
/// One foldable node — a JSON object/array's brace-to-brace span, or an XML element's
/// open-tag-to-close-tag span — as the [Start, End) range of <em>hidden</em> text a collapse
/// would replace with a placeholder. Pure text analysis with no UI dependency; the Formatter's
/// outline mode is the one consumer today.
/// </summary>
public readonly record struct FoldRegion(int Start, int End);

/// <summary>
/// Finds every foldable JSON/XML node in a document by walking <see cref="JsonTokenizer"/>'s or
/// <see cref="XmlTokenizer"/>'s own output rather than re-scanning the raw text — reusing them is
/// what makes this safe against brackets inside string values or attribute values without having
/// to reimplement escape handling here too.
/// </summary>
public static class FoldFinder
{
    /// <summary>Every foldable node in <paramref name="text"/> for <paramref name="formatId"/>
    /// ("json" or "xml") whose content spans more than one line — a one-liner has nothing worth
    /// hiding. Any other format id yields nothing to fold.</summary>
    public static List<FoldRegion> Find(string text, string formatId) => formatId switch
    {
        "json" => FindJsonFolds(text),
        "xml" => FindXmlFolds(text),
        _ => new List<FoldRegion>(),
    };

    /// <summary>
    /// Matches every <c>{</c>/<c>[</c> to its closing <c>}</c>/<c>]</c> with a simple depth stack
    /// over the tokenizer's punctuation tokens. An unmatched close (malformed JSON) is just
    /// skipped — the same "never throw, do the best you can" contract the tokenizer itself keeps.
    /// </summary>
    public static List<FoldRegion> FindJsonFolds(string text)
    {
        var regions = new List<FoldRegion>();
        var stack = new Stack<int>();
        foreach (var token in JsonTokenizer.Instance.Tokenize(text))
        {
            if (token.Kind != TokenKind.Punctuation)
                continue;
            char c = text[token.Start];
            if (c is '{' or '[')
            {
                stack.Push(token.End);
            }
            else if (c is '}' or ']')
            {
                if (stack.Count == 0)
                    continue;
                int start = stack.Pop();
                int end = token.Start;
                if (end > start && text.AsSpan(start, end - start).Contains('\n'))
                    regions.Add(new FoldRegion(start, end));
            }
        }
        return regions;
    }

    /// <summary>
    /// Matches every opening element tag to its closing tag with a name-checked stack. For each
    /// <c>&lt;</c> punctuation token this scans forward only as far as that same tag's own
    /// closing <c>&gt;</c> — noting a <c>/</c> just before it as self-closing — so attributes and
    /// quoted attribute values are walked past without needing their own cases here. A
    /// self-closing tag is never pushed (nothing to fold); a closing tag that doesn't match the
    /// name on top of the stack is left alone rather than guessed at, the same best-effort spirit
    /// as <see cref="FindJsonFolds"/>.
    /// </summary>
    public static List<FoldRegion> FindXmlFolds(string text)
    {
        var regions = new List<FoldRegion>();
        var tokens = XmlTokenizer.Instance.Tokenize(text);
        var stack = new Stack<(string Name, int ContentStart)>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var open = tokens[i];
            if (open.Kind != TokenKind.Punctuation || text[open.Start] != '<')
                continue;

            bool closing = i + 1 < tokens.Count
                && tokens[i + 1].Kind == TokenKind.Punctuation
                && text[tokens[i + 1].Start] == '/';
            int nameIndex = closing ? i + 2 : i + 1;
            if (nameIndex >= tokens.Count || tokens[nameIndex].Kind != TokenKind.TagName)
                continue;
            string name = text.Substring(tokens[nameIndex].Start, tokens[nameIndex].Length);

            int j = nameIndex + 1;
            bool selfClosing = false;
            while (j < tokens.Count)
            {
                char pc = tokens[j].Kind == TokenKind.Punctuation ? text[tokens[j].Start] : '\0';
                if (pc == '>')
                    break;
                if (pc == '/')
                    selfClosing = true;
                j++;
            }
            if (j >= tokens.Count)
                break; // unterminated tag at end of document — nothing further to match

            if (closing)
            {
                if (stack.Count > 0 && stack.Peek().Name == name)
                {
                    var (_, contentStart) = stack.Pop();
                    int contentEnd = open.Start;
                    if (contentEnd > contentStart && text.AsSpan(contentStart, contentEnd - contentStart).Contains('\n'))
                        regions.Add(new FoldRegion(contentStart, contentEnd));
                }
            }
            else if (!selfClosing)
            {
                stack.Push((name, tokens[j].End));
            }

            i = j; // resume right after this tag's own '>'
        }

        return regions;
    }
}
