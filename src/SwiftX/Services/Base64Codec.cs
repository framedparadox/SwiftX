using System.Text;

namespace SwiftX.Services;

/// <summary>
/// Base64 encode/decode logic split out from <c>Base64Page</c> — same reasoning as
/// <see cref="ValueMasking"/>: a static, unit-testable core behind a thin UI wrapper.
/// </summary>
public static class Base64Codec
{
    public readonly record struct Result(bool Success, string Text, string Error);

    /// <summary>UTF-8 encode, then Base64. URL-safe swaps +/ for -_ and drops padding.</summary>
    public static Result Encode(string text, bool urlSafe)
    {
        if (text.Length == 0)
            return new Result(true, "", "");

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        if (urlSafe)
            encoded = encoded.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return new Result(true, encoded, "");
    }

    /// <summary>
    /// Decodes Base64 back to UTF-8 text. Deliberately forgiving on the way in — the same
    /// "clean up what was pasted" philosophy as <see cref="Formats.InputSanitizer"/>: strips
    /// whitespace/newlines a MIME-wrapped or copy-pasted token picks up, accepts either
    /// alphabet (-_ or +/) regardless of how it was encoded, and restores padding — but never
    /// silent on the way out: bytes that aren't valid UTF-8 are reported, not replaced with
    /// U+FFFD.
    /// </summary>
    public static Result Decode(string text)
    {
        if (text.Length == 0)
            return new Result(true, "", "");

        var normalized = Normalize(text);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            return new Result(false, "", Loc.Get("Base64.InvalidInput"));
        }

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return new Result(true, strict.GetString(bytes), "");
        }
        catch (DecoderFallbackException)
        {
            return new Result(false, "", Loc.Get("Base64.NotUtf8"));
        }
    }

    /// <summary>
    /// A quick charset + length heuristic — good enough to drive the detected-format label
    /// and the clipboard-chip check, not a validator (that's what <see cref="Decode"/> is for).
    /// </summary>
    public static bool IsLikelyBase64(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 8)
            return false;

        int significant = 0;
        int padding = 0;
        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
                continue;
            if (c == '=')
            {
                padding++;
                continue;
            }
            // Padding, if any, only ever trails the significant characters.
            if (padding > 0)
                return false;
            bool isBase64Char = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                || c is '+' or '/' or '-' or '_';
            if (!isBase64Char)
                return false;
            significant++;
        }

        // A base64 group of 4 characters encodes 3 bytes, so an unpadded string's length can be
        // a multiple of 4, or short by one (2 or 3) — never short by exactly one character (1).
        return significant >= 8 && significant % 4 != 1;
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
                continue;
            sb.Append(c switch
            {
                '-' => '+',
                '_' => '/',
                _ => c,
            });
        }

        int pad = sb.Length % 4;
        if (pad != 0)
            sb.Append('=', 4 - pad);
        return sb.ToString();
    }
}
