using System.Collections.Generic;
using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Confirms every key in <c>Loc.EnglishUS.cs</c> carries a real, distinct translation in each of
/// this batch's five new languages — Ukrainian, Greek, Thai, Czech and Romanian — rather than
/// silently falling back to the English-US string via <see cref="Loc.Get"/>.
/// <para>
/// Comparing against the literal English-US value (not just <c>!= key</c>) is what makes this
/// strict: <see cref="Loc.Get"/> falls back to the English-US string, not the raw key, when a
/// language's table is missing an entry, so a weaker check would not catch a missing translation.
/// </para>
/// <para>
/// <see cref="Loc"/> holds its active language as static state shared across tests, so — like
/// <c>LocTests</c> and <c>SettingsLocalizationGroupATests</c> — this restores
/// <see cref="AppLanguage.EnglishUS"/> in a <c>finally</c>, and belongs to
/// <see cref="LocalizationCollection"/> so it never races <c>GreetingTests</c> or the other
/// localization suites over that same state.
/// </para>
/// </summary>
[Collection(LocalizationCollection.Name)]
public class NewLanguagesGroupATests
{
    private static readonly AppLanguage[] Languages =
    {
        AppLanguage.Ukrainian,
        AppLanguage.Greek,
        AppLanguage.Thai,
        AppLanguage.Czech,
        AppLanguage.Romanian,
    };

    /// <summary>Every key in <c>Loc.EnglishUS.cs</c>, in the same order the file declares them.</summary>
    private static readonly string[] Keys =
    {
        "Tray.Tooltip",
        "Tray.Show",
        "Tray.HideDock",
        "Tray.AddNew",
        "Tray.Settings",
        "Tray.Quit",
        "Settings.Hotkey",
        "Greeting.Morning",
        "Greeting.Afternoon",
        "Greeting.Evening",
        "Hotkey.Press",
        "Hotkey.None",
        "Tool.CopyOutput",
        "Tool.Save",
        "Tool.Clear",
        "Tool.Validate",
        "Tool.Run",
        "Tool.SwapInOut",
        "Tool.Format",
        "Tool.Minify",
        "Tool.DiscardTitle",
        "Tool.DiscardBody",
        "Tool.Discard",
        "Common.Cancel",
        "Common.Input",
        "Formatter.InputPlaceholder",
        "Formatter.Indent",
        "Formatter.FormatName",
        "Formatter.SortKeys",
        "Formatter.Escape",
        "Formatter.Unescape",
        "Formatter.Convert",
        "Formatter.ToJson",
        "Formatter.ToXml",
        "Formatter.Escaped",
        "Formatter.SortKeysJsonOnly",
        "Formatter.Search",
        "Formatter.SearchPlaceholder",
        "Formatter.SearchPrevious",
        "Formatter.SearchNext",
        "Formatter.SearchClose",
        "Formatter.SearchMatchCount",
        "Formatter.SearchNoMatches",
        "Formatter.Outline",
        "Formatter.OutlineActive",
        "Formatter.OutlineNothingToFold",
        "Tool.Advanced",
        "Tool.PrivacyFooter",
        "Tool.SavedTo",
        "Tool.SaveFailed",
        "Tool.FileUnreadable",
        "Tool.FileTooLarge",
        "Status.Counts",
        "Status.Valid",
        "Tool.InvalidAt",
        "Format.Auto",
        "Format.Yaml",
        "Format.Csv",
        "QuickFormat.InputPlaceholder",
        "QuickFormat.OpenDataFormatter",
        "QuickFormat.PlainText",
        "QuickFormat.InvalidFormat",
        "QuickFormat.InvalidData",
        "QuickFormat.TooLarge",
        "SecureValues.Heading",
        "SecureValues.InfoLabel",
        "SecureValues.Blurb",
        "SecureValues.Add",
        "SecureValues.Key",
        "SecureValues.Value",
        "SecureValues.ValueHeader",
        "SecureValues.KeyPlaceholder",
        "SecureValues.ValuePlaceholder",
        "SecureValues.Save",
        "SecureValues.Copy",
        "SecureValues.Edit",
        "SecureValues.Delete",
        "SecureValues.Refresh",
        "SecureValues.KeyRequired",
        "SecureValues.Saved",
        "SecureValues.Copied",
        "SecureValues.Count",
        "Base64.InputPlaceholder",
        "Base64.Encode",
        "Base64.Decode",
        "Base64.UrlSafe",
        "Base64.DetectedText",
        "Base64.DetectedBase64",
        "Base64.InvalidInput",
        "Base64.NotUtf8",
        "Base64.Encoded",
        "Base64.Decoded",
        "Base64.FromFile",
        "Common.Yes",
        "Common.No",
        "Certificate.OpenFile",
        "Certificate.Empty",
        "Certificate.Unrecognized",
        "Certificate.EmptyKeystore",
        "Certificate.KeystoreTruncated",
        "Certificate.PasswordRequired",
        "Certificate.WrongPassword",
        "Certificate.PasswordPlaceholder",
        "Certificate.Unlock",
        "Certificate.Count",
        "Certificate.NotYetValid",
        "Certificate.Expired",
        "Certificate.ExpiresIn",
        "Certificate.ExpiresInValue",
        "Certificate.ExpiredValue",
        "Certificate.ValidBadge",
        "Certificate.Copied",
        "Certificate.CopyField",
        "Certificate.Field.Subject",
        "Certificate.Field.CommonName",
        "Certificate.Field.Issuer",
        "Certificate.Field.Serial",
        "Certificate.Field.Version",
        "Certificate.Field.NotBefore",
        "Certificate.Field.NotAfter",
        "Certificate.Field.ExpiresIn",
        "Certificate.Field.Expired",
        "Certificate.Field.StartsIn",
        "Certificate.Field.SignatureAlgorithm",
        "Certificate.Field.PublicKey",
        "Certificate.Field.Sha1",
        "Certificate.Field.Sha256",
        "Certificate.Field.HasPrivateKey",
        "Certificate.Field.KeyUsage",
        "Certificate.Field.EnhancedKeyUsage",
        "Certificate.Field.BasicConstraints",
        "Certificate.Field.SubjectAltNames",
        "Certificate.Field.SubjectKeyId",
        "Certificate.Field.AuthorityKeyId",
        "OrbitLayout.HowTo",
        "OrbitLayout.Help.RunOnboarding",
        "OrbitLayout.Help.ChooseWhatGoesOn",
        "OrbitLayout.Help.ReadHandbook",
        "OrbitLayout.Title",
        "OrbitLayout.AddItem",
        "OrbitLayout.ResetDefaults",
        "OrbitLayout.AddToOrbit.Eyebrow",
        "OrbitLayout.AddToOrbit.Replace",
        "OrbitLayout.AddToOrbit.About",
        "OrbitLayout.AddToOrbit.Caption",
        "OrbitLayout.AddToOrbit.ResetName",
        "OrbitLayout.AddToOrbit.DropToAdd",
        "OrbitLayout.AddToOrbit.FillEmpty",
        "OrbitLayout.AddToOrbit.ReplacingSummary",
        "OrbitLayout.AddToOrbit.Full",
        "OrbitLayout.AddToOrbit.OneFreeSlot",
        "OrbitLayout.AddToOrbit.FreeSlots",
        "OrbitLayout.Settings.Eyebrow",
        "OrbitLayout.ItemSize.Heading",
        "OrbitLayout.ItemSize.About",
        "OrbitLayout.ItemSize.Info",
        "OrbitLayout.ItemSize.Small",
        "OrbitLayout.ItemSize.Medium",
        "OrbitLayout.ItemSize.Large",
        "OrbitLayout.ItemSize.NameTemplate",
        "OrbitLayout.ItemShape.Heading",
        "OrbitLayout.ItemShape.About",
        "OrbitLayout.ItemShape.Info",
        "OrbitLayout.ItemShape.Circle",
        "OrbitLayout.ItemShape.Squircle",
        "OrbitLayout.ItemShape.Square",
        "OrbitLayout.ItemShape.Hexagon",
        "OrbitLayout.ItemShape.NameTemplate",
        "OrbitLayout.ItemShape.SquircleTooltip",
        "OrbitLayout.ItemShape.HexagonTooltip",
        "OrbitLayout.Radius.Heading",
        "OrbitLayout.Radius.About",
        "OrbitLayout.Radius.Info",
        "OrbitLayout.Radius.ResetName",
        "OrbitLayout.Radius.ResetTooltip",
        "OrbitLayout.Radius.SliderName",
        "OrbitLayout.Radius.Value",
        "OrbitLayout.HotCorner.Heading",
        "OrbitLayout.HotCorner.About",
        "OrbitLayout.HotCorner.Info",
        "OrbitLayout.HotCorner.ToggleName",
        "OrbitLayout.HotCorner.Orbit",
        "OrbitLayout.HotCorner.Quadrant",
        "OrbitLayout.HoverStyle.Heading",
        "OrbitLayout.HoverStyle.About",
        "OrbitLayout.HoverStyle.Info",
        "OrbitLayout.HoverStyle.Invert",
        "OrbitLayout.HoverStyle.Zoom",
        "OrbitLayout.HoverStyle.Tint",
        "OrbitLayout.HoverStyle.Animate",
        "OrbitLayout.HoverStyle.NameTemplate",
        "OrbitLayout.HoverStyle.ZoomIn",
        "OrbitLayout.HoverStyle.ZoomOut",
        "OrbitLayout.HoverStyle.ZoomDirectionNameTemplate",
        "OrbitLayout.HoverStyle.ZoomGrowingTooltip",
        "OrbitLayout.HoverStyle.ZoomShrinkingTooltip",
        "OrbitLayout.HoverStyle.HoverColor",
        "OrbitLayout.HoverStyle.HoverColorPicker",
        "Common.ResetToDefault",
        "Settings.Nav.General",
        "Settings.Nav.Support",
        "Settings.Nav.Privacy",
        "Settings.Nav.About",
        "Settings.Nav.BackToHome",
        "Settings.General.Header",
        "Settings.General.DesktopOffer.Title",
        "Settings.General.DesktopOffer.Message",
        "Settings.General.DesktopOffer.CreateShortcut",
        "Settings.General.ColorTheme",
        "Settings.General.Theme.FollowOs",
        "Settings.General.Theme.Light",
        "Settings.General.Theme.Dark",
        "Settings.General.Theme.Midnight",
        "Settings.General.Theme.FrostedGlass",
        "Settings.General.Frost.Dark",
        "Settings.General.Frost.Medium",
        "Settings.General.Frost.Light",
        "Settings.General.Frost.NameTemplate",
        "Settings.General.Hotkey.Body",
        "Settings.General.Hotkey.ToggleName",
        "Settings.General.Hotkey.NotRegistered",
        "Settings.General.Hotkey.NeedsModifier",
        "Settings.General.Language",
        "Settings.General.AccentColor",
        "Settings.General.AccentColorPicker",
        "Settings.General.StartWithWindows",
        "Settings.General.DesktopShortcut",
        "Settings.General.BackupRestore.Title",
        "Settings.General.BackupRestore.Body",
        "Settings.General.BackupRestore.Export",
        "Settings.General.BackupRestore.Import",
        "Settings.General.Reset.Title",
        "Settings.General.Reset.Body",
        "Settings.Mouse.Title",
        "Settings.Mouse.Body",
        "Settings.Mouse.ButtonName",
        "Settings.Mouse.GestureName",
        "Settings.Mouse.ToggleName",
        "Settings.Mouse.LeftButton",
        "Settings.Mouse.RightButton",
        "Settings.Mouse.MiddleButton",
        "Settings.Mouse.BackButton",
        "Settings.Mouse.ForwardButton",
        "Settings.Mouse.LongPress",
        "Settings.Mouse.DoubleClick",
        "Settings.Feedback.Header",
        "Settings.Feedback.SetupGuide.Heading",
        "Settings.Feedback.SetupGuide.Intro",
        "Settings.Feedback.SetupGuide.Step1",
        "Settings.Feedback.SetupGuide.Step2",
        "Settings.Feedback.SetupGuide.Step3",
        "Settings.Feedback.SetupGuide.OpenGuide",
        "Settings.Feedback.Troubleshooting.Heading",
        "Settings.Feedback.Troubleshooting.Body",
        "Settings.Feedback.Troubleshooting.Link",
        "Settings.Feedback.Footer",
        "Settings.Feedback.ReadPrivacy",
        "Settings.Privacy.Header",
        "Settings.Privacy.Device.Heading",
        "Settings.Privacy.Device.Body",
        "Settings.Privacy.NoTracking.Heading",
        "Settings.Privacy.NoTracking.Body",
        "Settings.Privacy.SecureValues.Heading",
        "Settings.Privacy.SecureValues.Body",
        "Settings.Privacy.ToolsOffline.Heading",
        "Settings.Privacy.ToolsOffline.Body",
        "Settings.Privacy.Network.Heading",
        "Settings.Privacy.Network.Body",
        "Settings.Privacy.TakeItBack.Heading",
        "Settings.Privacy.TakeItBack.Body",
        "Settings.Privacy.Rating.Heading",
        "Settings.Privacy.Rating.Body",
        "Settings.About.Header",
        "Settings.About.Blurb",
        "Settings.About.Version",
        "Settings.About.Framework",
        "Settings.About.TermsAndConditions",
        "Settings.About.FeedbackRating.Title",
        "Settings.About.FeedbackRating.Body",
        "Settings.About.FeedbackRating.ShareFeedback",
        "Settings.About.FeedbackRating.RateOnStore",
        "Settings.About.FeedbackRating.StarOnGitHub",
    };

    /// <summary>
    /// (language, key) pairs where this batch's translation is deliberately spelled the same as
    /// the English-US value — a genuine cognate, an established loanword, or a key with no
    /// language-specific content to translate in the first place — never an oversight:
    /// <list type="bullet">
    /// <item><c>Tray.Tooltip</c> (the brand name "SwiftX"), <c>Formatter.FormatName</c> (a bare
    /// "{0}" template with no surrounding words), <c>Format.Yaml</c>/<c>Format.Csv</c> (file-format
    /// acronyms), <c>Certificate.Field.CommonName</c> ("CN", the X.509 abbreviation), and
    /// <c>Settings.About.Framework</c> (the "WinUI 3 • {0} • {1}" version/build line) carry no
    /// language-specific content to translate in any of these languages.</item>
    /// <item><c>OrbitLayout.Radius.Value</c> ("{0} px") uses the universal "px" unit abbreviation,
    /// which both Greek and Czech software UI also uses verbatim rather than a localized unit
    /// name.</item>
    /// <item>Romanian, being the one Romance language in this batch, shares several true cognates
    /// with English UI vocabulary: <c>Status.Valid</c> ("Valid"), <c>QuickFormat.InvalidFormat</c>
    /// ("invalid"), <c>OrbitLayout.ItemShape.Hexagon</c> ("Hexagon"), <c>OrbitLayout.Radius.Value</c>
    /// ("{0} px"), <c>OrbitLayout.HoverStyle.Zoom</c> ("Zoom", an established loanword in Romanian
    /// tech vocabulary — matching how French/German/Spanish/Italian/Portuguese/Dutch already tie on
    /// this same key in <c>SettingsLocalizationGroupATests</c>), <c>Settings.Nav.General</c>
    /// ("GENERAL") and <c>Settings.General.Header</c> ("General").</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<(AppLanguage Language, string Key)> KnownTies = new()
    {
        (AppLanguage.Ukrainian, "Tray.Tooltip"),
        (AppLanguage.Greek, "Tray.Tooltip"),
        (AppLanguage.Thai, "Tray.Tooltip"),
        (AppLanguage.Czech, "Tray.Tooltip"),
        (AppLanguage.Romanian, "Tray.Tooltip"),

        (AppLanguage.Ukrainian, "Formatter.FormatName"),
        (AppLanguage.Greek, "Formatter.FormatName"),
        (AppLanguage.Thai, "Formatter.FormatName"),
        (AppLanguage.Czech, "Formatter.FormatName"),
        (AppLanguage.Romanian, "Formatter.FormatName"),

        (AppLanguage.Ukrainian, "Format.Yaml"),
        (AppLanguage.Greek, "Format.Yaml"),
        (AppLanguage.Thai, "Format.Yaml"),
        (AppLanguage.Czech, "Format.Yaml"),
        (AppLanguage.Romanian, "Format.Yaml"),

        (AppLanguage.Ukrainian, "Format.Csv"),
        (AppLanguage.Greek, "Format.Csv"),
        (AppLanguage.Thai, "Format.Csv"),
        (AppLanguage.Czech, "Format.Csv"),
        (AppLanguage.Romanian, "Format.Csv"),

        (AppLanguage.Ukrainian, "Certificate.Field.CommonName"),
        (AppLanguage.Greek, "Certificate.Field.CommonName"),
        (AppLanguage.Thai, "Certificate.Field.CommonName"),
        (AppLanguage.Czech, "Certificate.Field.CommonName"),
        (AppLanguage.Romanian, "Certificate.Field.CommonName"),

        (AppLanguage.Ukrainian, "Settings.About.Framework"),
        (AppLanguage.Greek, "Settings.About.Framework"),
        (AppLanguage.Thai, "Settings.About.Framework"),
        (AppLanguage.Czech, "Settings.About.Framework"),
        (AppLanguage.Romanian, "Settings.About.Framework"),

        (AppLanguage.Greek, "OrbitLayout.Radius.Value"),
        (AppLanguage.Czech, "OrbitLayout.Radius.Value"),
        (AppLanguage.Romanian, "OrbitLayout.Radius.Value"),

        (AppLanguage.Romanian, "Status.Valid"),
        (AppLanguage.Romanian, "QuickFormat.InvalidFormat"),
        (AppLanguage.Romanian, "OrbitLayout.ItemShape.Hexagon"),
        (AppLanguage.Romanian, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.Romanian, "Settings.Nav.General"),
        (AppLanguage.Romanian, "Settings.General.Header"),
    };

    [Fact]
    public void Every_assigned_language_translates_every_key()
    {
        try
        {
            Loc.Initialize(AppLanguage.EnglishUS);
            var englishValues = new Dictionary<string, string>();
            foreach (var key in Keys)
                englishValues[key] = Loc.Get(key);

            foreach (var language in Languages)
            {
                Loc.Initialize(language);
                Assert.Equal(language, Loc.CurrentLanguage);

                foreach (var key in Keys)
                {
                    if (KnownTies.Contains((language, key))) continue;
                    Assert.NotEqual(englishValues[key], Loc.Get(key));
                }
            }
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }
}
