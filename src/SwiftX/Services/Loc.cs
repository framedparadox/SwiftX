using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// String table lookup, keyed by short dotted keys (e.g. "Tray.Show") rather than the English text
/// itself, so every call site stays stable while the text behind it changes per language.
/// <para>
/// Each language's table lives in its own partial-class file under <c>Services/Localization</c>, as
/// a full, independent dictionary literal — including <see cref="EnglishUK"/>, even though it
/// differs from <see cref="EnglishUS"/> (the neutral/base table) in only a couple of entries. See
/// <see cref="EnglishUS"/>'s own doc comment for why: none of these tables are built as overrides on
/// top of another at static-init time. <see cref="Get"/> falls back to <see cref="EnglishUS"/> for
/// any key the current language's table doesn't carry, so a partial translation degrades to English
/// for the missing rows instead of showing raw keys.
/// </para>
/// <para>
/// Switching language at runtime (<see cref="Initialize"/>) takes effect immediately for anything
/// that calls <see cref="Get"/>/<see cref="Format"/> when it builds its UI — the tool pages
/// (Formatter, Base64, Certificate, Secure values) construct fresh each time their window opens, and
/// the tray menu rebuilds on every click, so both already re-read this table live. Windows built
/// once from XAML with literal English text (Settings itself, Add item, the orbit's own tooltips)
/// do not — those still need a restart to pick up a language change.
/// </para>
/// </summary>
public static partial class Loc
{
    // Lazily built on first use rather than as a field initializer: the per-language dictionaries
    // (EnglishUS, French, ...) are static readonly fields declared in this same partial class's
    // *other* files, and the C# spec leaves the relative order of field initializers across a
    // partial type's files unspecified. A field initializer here could run before theirs and
    // capture nulls; a lazily-evaluated property instead runs on first call to Get()/Initialize(),
    // by which point the type's static constructor — which covers every partial file — has always
    // already finished, regardless of that unspecified order.
    private static Dictionary<AppLanguage, Dictionary<string, string>>? _tableByLanguage;

    private static Dictionary<AppLanguage, Dictionary<string, string>> TableByLanguage =>
        _tableByLanguage ??= new()
        {
            [AppLanguage.EnglishUS] = EnglishUS,
            [AppLanguage.EnglishUK] = EnglishUK,
            [AppLanguage.French] = French,
            [AppLanguage.German] = German,
            [AppLanguage.Spanish] = Spanish,
            [AppLanguage.Italian] = Italian,
            [AppLanguage.PortugueseBR] = PortugueseBR,
            [AppLanguage.PortuguesePT] = PortuguesePT,
            [AppLanguage.Dutch] = Dutch,
            [AppLanguage.Polish] = Polish,
            [AppLanguage.Turkish] = Turkish,
            [AppLanguage.Russian] = Russian,
            [AppLanguage.Arabic] = Arabic,
            [AppLanguage.Hebrew] = Hebrew,
            [AppLanguage.Hindi] = Hindi,
            [AppLanguage.Japanese] = Japanese,
            [AppLanguage.ChineseSimplified] = ChineseSimplified,
            [AppLanguage.ChineseTraditional] = ChineseTraditional,
            [AppLanguage.Korean] = Korean,
        };

    /// <summary>The language currently in effect — always a concrete one, never
    /// <see cref="AppLanguage.System"/> (see <see cref="Initialize"/>).</summary>
    public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.EnglishUS;

    /// <summary>Whether the language currently in effect reads right-to-left (Arabic, Hebrew), so
    /// windows rendering localized content should flip their <c>FlowDirection</c>. Reads the
    /// already-resolved <see cref="CurrentLanguage"/>, so it never has to re-resolve System.</summary>
    public static bool CurrentIsRightToLeft => LanguageCatalog.Find(CurrentLanguage).IsRightToLeft;

    /// <summary>
    /// Sets the active table, resolving <see cref="AppLanguage.System"/> to a concrete language via
    /// <see cref="LanguageCatalog.ResolveEffective"/> first. Safe to call again later (from
    /// Settings, after the user changes the language) — there is no per-language setup beyond
    /// picking which dictionary <see cref="Get"/> reads from.
    /// </summary>
    public static void Initialize(AppLanguage language = AppLanguage.System) =>
        CurrentLanguage = LanguageCatalog.ResolveEffective(language);

    public static string Get(string key)
    {
        if (TableByLanguage.TryGetValue(CurrentLanguage, out var table) &&
            table.TryGetValue(key, out var value))
            return value;
        return EnglishUS.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }
}
