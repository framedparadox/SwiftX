using System.Globalization;
using System.Text;

namespace SwiftX.Services.Formats;

/// <summary>
/// Cleans messy pasted payloads before validate/format: outer quotes, leading/trailing
/// whitespace, and common escape sequences (\n, \r, \t, \", \\, \uXXXX) that show up when
/// JSON/XML is copied out of logs, string literals, or double-encoded API responses.
/// </summary>
public static class InputSanitizer
{
    /// <summary>Trim, strip enclosing quotes, and unescape control/quote sequences.</summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = text.Trim();
        result = StripEnclosingQuotes(result);
        result = Unescape(result);
        return result.Trim();
    }

    /// <summary>
    /// Removes one layer of surrounding single or double quotes when the whole payload is
    /// wrapped (including the common case of a quoted, escaped JSON string).
    /// </summary>
    public static string StripEnclosingQuotes(string text)
    {
        var current = text.Trim();
        // Peel at most a couple of layers — enough for "\"{…}\"" without eating legitimate
        // JSON string values that happen to be the whole document.
        for (int pass = 0; pass < 2; pass++)
        {
            if (current.Length < 2)
                break;

            char first = current[0];
            char last = current[^1];
            if ((first != '"' && first != '\'') || first != last)
                break;

            // Only strip when the matching closer is the document closer, not an interior quote.
            if (!IsBalancedOuterQuote(current, first))
                break;

            current = current[1..^1].Trim();
        }
        return current;
    }

    /// <summary>Turns literal escape sequences into their characters (\\n → newline, etc.).</summary>
    public static string Unescape(string text)
    {
        if (text.IndexOf('\\') < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                sb.Append(c);
                continue;
            }

            char next = text[i + 1];
            switch (next)
            {
                case 'n':
                    sb.Append('\n');
                    i++;
                    break;
                case 'r':
                    sb.Append('\r');
                    i++;
                    break;
                case 't':
                    sb.Append('\t');
                    i++;
                    break;
                case 'b':
                    sb.Append('\b');
                    i++;
                    break;
                case 'f':
                    sb.Append('\f');
                    i++;
                    break;
                case '"':
                case '\'':
                case '\\':
                case '/':
                    sb.Append(next);
                    i++;
                    break;
                case 'u' when i + 5 < text.Length
                              && IsHex(text[i + 2]) && IsHex(text[i + 3])
                              && IsHex(text[i + 4]) && IsHex(text[i + 5]):
                    int code = int.Parse(text.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    sb.Append((char)code);
                    i += 5;
                    break;
                default:
                    // Unknown escape — drop the backslash (remove the escape character) and keep
                    // the following character, matching the "strip escape characters" intent.
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool IsBalancedOuterQuote(string text, char quote)
    {
        // Walk the interior; if we can reach the final char as the matching unescaped closer,
        // the outer pair is a wrapper rather than part of the payload.
        bool escaped = false;
        for (int i = 1; i < text.Length - 1; i++)
        {
            char c = text[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
            if (c == quote)
                return false;
        }
        return text[^1] == quote;
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
