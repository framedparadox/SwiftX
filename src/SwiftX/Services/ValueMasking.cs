namespace SwiftX.Services;

/// <summary>Masks secret values for on-screen display while keeping copy/export intact.</summary>
public static class ValueMasking
{
    private const char MaskChar = '•';

    /// <summary>
    /// Length ≥ 8: first 3 + mask + last 3.
    /// Length 5–7: first 3 + mask + last 1.
    /// Length &lt; 5: first 3 only (remainder masked).
    /// </summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        int len = value.Length;
        if (len <= 3)
            return value;

        if (len < 5)
            return value[..3] + new string(MaskChar, len - 3);

        if (len < 8)
            return value[..3] + new string(MaskChar, len - 4) + value[^1];

        return value[..3] + new string(MaskChar, len - 6) + value[^3..];
    }
}
