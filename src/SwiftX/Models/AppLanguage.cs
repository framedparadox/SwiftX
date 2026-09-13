namespace SwiftX.Models;

/// <summary>
/// The UI language SwiftX renders in. Persisted on <see cref="AppConfig"/> and resolved by
/// <see cref="Services.LanguageCatalog"/> / applied by <see cref="Services.Loc"/>.
/// <para>
/// English (US) is the neutral/fallback language — every other table falls back to it for any
/// key it does not carry (see <see cref="Services.Loc.Get"/>), so a partially translated table is
/// never fatal, it just shows English for the untranslated rows.
/// </para>
/// <para>
/// Persisted by name via <c>JsonStringEnumConverter</c> (see <c>OrbitStore</c>), so these member
/// names are a storage contract: rename one and every config that already wrote the old name is
/// stranded. Add new languages by appending members, never by renaming existing ones.
/// </para>
/// </summary>
public enum AppLanguage
{
    /// <summary>Follow the OS's current UI language, falling back to English (US) when Windows is
    /// set to a language SwiftX doesn't carry a table for. See <see cref="Services.LanguageCatalog.ResolveEffective"/>.</summary>
    System,
    EnglishUS,
    EnglishUK,
    French,
    German,
    Spanish,
    Italian,
    PortugueseBR,
    Japanese,
    ChineseSimplified,
    Korean,
    Russian,
    Arabic,
    Hebrew,
    Hindi,
    Dutch,
    Polish,
    Turkish,
    ChineseTraditional,
    PortuguesePT,
}
