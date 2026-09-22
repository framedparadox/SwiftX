using SwiftX.Services.Syntax;

namespace SwiftX.Services.Formats;

/// <summary>
/// What a quick, no-options look at a payload should put on screen: the text to show, the format
/// it was recognized as (null for "this was never structured data"), and the parse failure to
/// warn about when it was recognizable but broken.
/// </summary>
/// <param name="Text">The text to display — the formatted document on success, otherwise the
/// caller's own text verbatim. Invalid input never clears the user's text (design doc §14.1).</param>
/// <param name="Format">The format whose syntax highlighting applies, or null for plain text.</param>
/// <param name="IsInvalid">True when <paramref name="Text"/> looks like <paramref name="Format"/>
/// but failed to parse — the case that swaps Copy out for the warning indicator.</param>
/// <param name="Problem">The first diagnostic behind <paramref name="IsInvalid"/>, when the parser
/// gave one. A format can refuse a document without pinpointing a line (or throw outright), so this
/// is null more often than <paramref name="IsInvalid"/> is false.</param>
public readonly record struct QuickFormatResult(
    string Text, IDataFormat? Format, bool IsInvalid, Diagnostic? Problem)
{
    /// <summary>The tokenizer to colour <see cref="Text"/> with — the detected format's, or the
    /// no-op one when nothing was detected.</summary>
    public ITokenizer Tokenizer => Format?.Tokenizer ?? PlainTokenizer.Instance;

    internal static QuickFormatResult Plain(string text) => new(text, null, false, null);

    internal static QuickFormatResult Formatted(string text, IDataFormat format) =>
        new(text, format, false, null);

    internal static QuickFormatResult Invalid(string text, IDataFormat format, Diagnostic? problem) =>
        new(text, format, true, problem);
}

/// <summary>
/// The whole of Quick Format's "what do I do with this text" decision, with no UI attached:
/// detect, format, and fall back. Split out of <c>QuickFormatPage</c> for two reasons.
/// <para>
/// <b>It is the work that has to leave the UI thread.</b> Detection scans the document, the parse
/// builds a whole tree and the writer builds a whole new string — on a multi-megabyte paste that is
/// seconds of pure computation, and Quick Format runs it unprompted, on window open and on every
/// paste. As a static function of a string it can be handed to <see cref="System.Threading.Tasks.Task.Run(System.Action)"/>
/// unchanged; every format in <see cref="FormatRegistry"/> is a stateless singleton, so there is
/// nothing here for two overlapping runs to corrupt.
/// </para>
/// <para>
/// <b>It is also the part worth testing.</b> A WinUI page cannot be constructed in a unit test;
/// this can, so the fallback rules below (and the guarantee that none of them throw) are pinned by
/// <c>SwiftX.Tests.Formats.QuickFormatterTests</c> rather than by hand.
/// </para>
/// </summary>
public static class QuickFormatter
{
    /// <summary>
    /// Formats <paramref name="text"/> for a one-glance view. Never throws: every outcome —
    /// including a format that failed in a way its own <c>Format</c> does not describe — is a
    /// <see cref="QuickFormatResult"/>.
    /// </summary>
    public static QuickFormatResult Analyze(string text)
    {
        if (string.IsNullOrEmpty(text))
            return QuickFormatResult.Plain("");

        // No InputSanitizer.Clean here (unlike FormatterPage's explicit, user-triggered "Validate
        // & Clean" action): Clean's Unescape step turns any backslash-letter pair it doesn't
        // recognize into just the letter, which silently mangles ordinary clipboard text that was
        // never escaped JSON/XML in the first place — a Windows path such as
        // C:\Users\name\notes.csv loses its backslashes the instant this tool is opened or pasted
        // into, with nothing to signal that it happened. Detect and format the clipboard text
        // exactly as copied.
        IDataFormat format;
        int confidence;
        FormatResult result;
        try
        {
            (format, confidence) = FormatRegistry.Detect(text);
            result = format.Format(text, FormatOptions.Default);
        }
        catch (Exception ex)
        {
            // Every format's Format catches the exception type its own parser is documented to
            // raise (JsonException, XmlException, YamlParseException) and nothing else, and the
            // gap is not theoretical: System.Text.Json transcodes a string to UTF-8 before it
            // parses anything, and text that is not valid UTF-16 — a lone surrogate, which is
            // exactly what a truncated copy puts on the clipboard — fails in that step with
            // ArgumentException ("Cannot transcode invalid UTF-16 string to UTF-8 JSON text."),
            // straight past JsonFormat's catch of JsonException. An allocation failure, or a
            // writer refusing a document its own reader accepted, gets out the same way.
            //
            // On this path that exception had nowhere to go: it faulted a discarded Task, which
            // the runtime swallows, so the whole failure surfaced as a Quick Format window that
            // opened and then simply never showed anything, with no message and nothing to retry.
            // Showing the text as plain is what this method already does for input that was never
            // structured data, and it is always better than showing nothing at all.
            Services.Diag.Log($"QuickFormatter.Analyze: {ex.GetType().Name}: {ex.Message}");
            return QuickFormatResult.Plain(text);
        }

        if (result.Success)
            return QuickFormatResult.Formatted(result.Text, format);

        if (!LooksIntendedAsStructuredData(text, format, confidence))
            return QuickFormatResult.Plain(text);

        // Recognizably shaped as this format (braces, angle brackets, "key:" lines, comma rows)
        // but failed to parse — this is the "invalid data" case, distinct from plain prose above
        // that was never meant to be structured data in the first place.
        return QuickFormatResult.Invalid(text, format, result.Diagnostics.Count > 0 ? result.Diagnostics[0] : null);
    }

    /// <summary>
    /// Whether text that failed to parse as <paramref name="format"/> still looks like it was
    /// meant to be one, rather than being plain prose that was never structured data in the first
    /// place. <paramref name="confidence"/> alone isn't a reliable signal for this:
    /// <see cref="JsonFormat"/>'s in particular requires the outer braces/brackets to actually
    /// match before it scores above zero — which is exactly the property most broken JSON lacks (a
    /// missing closing brace, a paste truncated mid-document), so relying on it here silently
    /// downgraded the single most common "obviously broken JSON" shape to plain text instead of
    /// flagging it. A leading structural character — <c>{</c>/<c>[</c> for JSON, <c>&lt;</c> for
    /// XML — is a weaker but far more robust signal of intent, since it doesn't depend on the rest
    /// of the document being well-formed at all.
    /// </summary>
    private static bool LooksIntendedAsStructuredData(string text, IDataFormat format, int confidence)
    {
        if (confidence > 0)
            return true;

        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0)
            return false;

        return format.Id switch
        {
            "json" => trimmed[0] is '{' or '[',
            "xml" => trimmed[0] == '<',
            _ => false,
        };
    }
}
