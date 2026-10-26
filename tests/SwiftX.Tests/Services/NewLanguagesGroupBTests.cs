using System.Collections.Generic;
using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Confirms every key currently in <see cref="Loc.EnglishUS"/> (not just a recently-added subset)
/// carries a real, distinct translation in each of this batch's five languages — Vietnamese,
/// Indonesian, Swedish, Danish and Norwegian (Bokmål) — rather than silently falling back to the
/// English-US string via <see cref="Loc.Get"/>. A teammate is covering Ukrainian, Greek, Thai,
/// Czech and Romanian separately; those are out of scope here.
/// <para>
/// <see cref="Loc.Get"/> falls back to the English-US string — not the raw key — when a language's
/// table is missing an entry, so a bare <c>!= key</c> check would not catch a missing translation.
/// Comparing against the actual English-US value, as done below, is what actually proves a key was
/// translated rather than silently inherited.
/// </para>
/// <para>
/// This class depends on the <see cref="AppLanguage"/> members for these five languages and on
/// <see cref="Loc"/>'s <c>TableByLanguage</c> wiring them to the dictionaries in
/// <c>Loc.Vietnamese.cs</c>, <c>Loc.Indonesian.cs</c>, <c>Loc.Swedish.cs</c>, <c>Loc.Danish.cs</c>
/// and <c>Loc.Norwegian.cs</c> — work landing concurrently from another engineer. It will not
/// compile until that wiring exists.
/// </para>
/// <para>
/// <see cref="Loc"/> holds its active language as static state shared across tests, so (like
/// <c>LocTests</c>) this class restores <see cref="AppLanguage.EnglishUS"/> in a <c>finally</c>,
/// and belongs to <see cref="LocalizationCollection"/> so it never races <c>GreetingTests</c> or
/// the other localization test classes over that same state.
/// </para>
/// </summary>
[Collection(LocalizationCollection.Name)]
public class NewLanguagesGroupBTests
{
    private static readonly AppLanguage[] Languages =
    {
        AppLanguage.Vietnamese,
        AppLanguage.Indonesian,
        AppLanguage.Swedish,
        AppLanguage.Danish,
        AppLanguage.Norwegian,
    };

    /// <summary>Every key in <see cref="Loc.EnglishUS"/>, in the same order it appears there.</summary>
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
        "Settings.Mouse.None",
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
    /// the English-US value — never an oversight:
    /// <list type="bullet">
    /// <item><c>Tray.Tooltip</c> ("SwiftX") is the product name in every language.</item>
    /// <item><c>Formatter.FormatName</c> ("{0}"), <c>OrbitLayout.Radius.Value</c> ("{0} px") and
    /// <c>Settings.General.Frost.NameTemplate</c> ("Frost: {0}") carry no language-specific content
    /// beyond a template placeholder and/or a true cognate word for "frost"; likewise
    /// <c>Settings.About.Framework</c> ("WinUI 3  •  {0}  •  {1}") is a pure version/build string.</item>
    /// <item><c>Format.Yaml</c>, <c>Format.Csv</c> and <c>Certificate.Field.CommonName</c> ("CN")
    /// are data-format/standards acronyms with no translation in any of these languages.</item>
    /// <item><c>OrbitLayout.HotCorner.Orbit</c> ("Orbit") is the deliberately untranslated feature
    /// name, and <c>OrbitLayout.HoverStyle.Zoom</c> ("Zoom") is a universally borrowed term, in all
    /// five languages.</item>
    /// <item>The rest are true cognates or already-established loanwords in the specific
    /// language's everyday tech/OS vocabulary: Indonesian <c>Format</c>, <c>Input</c>,
    /// <c>Escape</c>, <c>Valid</c>, <c>Encode</c>, <c>Decode</c>; Swedish/Danish <c>Input</c>
    /// (Danish only), <c>Auto</c>, <c>Version</c>, <c>Support</c>; Swedish-only
    /// <c>Hexagon</c> and the preposition <c>In</c> (<c>ZoomIn</c>).</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<(AppLanguage Language, string Key)> KnownTies = new()
    {
        // Vietnamese
        (AppLanguage.Vietnamese, "Tray.Tooltip"),
        (AppLanguage.Vietnamese, "Formatter.FormatName"),
        (AppLanguage.Vietnamese, "Format.Yaml"),
        (AppLanguage.Vietnamese, "Format.Csv"),
        (AppLanguage.Vietnamese, "Certificate.Field.CommonName"),
        (AppLanguage.Vietnamese, "OrbitLayout.Radius.Value"),
        (AppLanguage.Vietnamese, "OrbitLayout.HotCorner.Orbit"),
        (AppLanguage.Vietnamese, "Settings.About.Framework"),

        // Indonesian
        (AppLanguage.Indonesian, "Tray.Tooltip"),
        (AppLanguage.Indonesian, "Tool.Format"),
        (AppLanguage.Indonesian, "Common.Input"),
        (AppLanguage.Indonesian, "Formatter.FormatName"),
        (AppLanguage.Indonesian, "Formatter.Escape"),
        (AppLanguage.Indonesian, "Status.Valid"),
        (AppLanguage.Indonesian, "Format.Yaml"),
        (AppLanguage.Indonesian, "Format.Csv"),
        (AppLanguage.Indonesian, "Base64.Encode"),
        (AppLanguage.Indonesian, "Base64.Decode"),
        (AppLanguage.Indonesian, "Certificate.Field.CommonName"),
        (AppLanguage.Indonesian, "OrbitLayout.Radius.Value"),
        (AppLanguage.Indonesian, "OrbitLayout.HotCorner.Orbit"),
        (AppLanguage.Indonesian, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.Indonesian, "Settings.About.Framework"),

        // Swedish
        (AppLanguage.Swedish, "Tray.Tooltip"),
        (AppLanguage.Swedish, "Formatter.FormatName"),
        (AppLanguage.Swedish, "Format.Auto"),
        (AppLanguage.Swedish, "Format.Yaml"),
        (AppLanguage.Swedish, "Format.Csv"),
        (AppLanguage.Swedish, "Certificate.Field.CommonName"),
        (AppLanguage.Swedish, "Certificate.Field.Version"),
        (AppLanguage.Swedish, "OrbitLayout.ItemShape.Hexagon"),
        (AppLanguage.Swedish, "OrbitLayout.Radius.Value"),
        (AppLanguage.Swedish, "OrbitLayout.HotCorner.Orbit"),
        (AppLanguage.Swedish, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.Swedish, "OrbitLayout.HoverStyle.ZoomIn"),
        (AppLanguage.Swedish, "Settings.Nav.Support"),
        (AppLanguage.Swedish, "Settings.General.Frost.NameTemplate"),
        (AppLanguage.Swedish, "Settings.Feedback.Header"),
        (AppLanguage.Swedish, "Settings.About.Version"),
        (AppLanguage.Swedish, "Settings.About.Framework"),

        // Danish
        (AppLanguage.Danish, "Tray.Tooltip"),
        (AppLanguage.Danish, "Common.Input"),
        (AppLanguage.Danish, "Formatter.FormatName"),
        (AppLanguage.Danish, "Format.Auto"),
        (AppLanguage.Danish, "Format.Yaml"),
        (AppLanguage.Danish, "Format.Csv"),
        (AppLanguage.Danish, "Certificate.Field.CommonName"),
        (AppLanguage.Danish, "Certificate.Field.Version"),
        (AppLanguage.Danish, "OrbitLayout.Radius.Value"),
        (AppLanguage.Danish, "OrbitLayout.HotCorner.Orbit"),
        (AppLanguage.Danish, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.Danish, "Settings.Nav.Support"),
        (AppLanguage.Danish, "Settings.General.Frost.NameTemplate"),
        (AppLanguage.Danish, "Settings.Feedback.Header"),
        (AppLanguage.Danish, "Settings.About.Version"),
        (AppLanguage.Danish, "Settings.About.Framework"),

        // Norwegian
        (AppLanguage.Norwegian, "Tray.Tooltip"),
        (AppLanguage.Norwegian, "Formatter.FormatName"),
        (AppLanguage.Norwegian, "Format.Auto"),
        (AppLanguage.Norwegian, "Format.Yaml"),
        (AppLanguage.Norwegian, "Format.Csv"),
        (AppLanguage.Norwegian, "Certificate.Field.CommonName"),
        (AppLanguage.Norwegian, "OrbitLayout.Radius.Value"),
        (AppLanguage.Norwegian, "OrbitLayout.HotCorner.Orbit"),
        (AppLanguage.Norwegian, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.Norwegian, "Settings.Nav.Support"),
        (AppLanguage.Norwegian, "Settings.General.Frost.NameTemplate"),
        (AppLanguage.Norwegian, "Settings.Feedback.Header"),
        (AppLanguage.Norwegian, "Settings.About.Framework"),
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
