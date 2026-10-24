using System.Collections.Generic;
using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Confirms the ~170 keys recently added for the Settings and Orbit Layout screens (see the
/// "Orbit Layout screen" / "Settings screen" sections of <c>Loc.EnglishUS.cs</c>) carry a real,
/// distinct translation in each of this batch's eight languages — French, German, Spanish, Italian,
/// Brazilian Portuguese, Dutch, Polish and Turkish — rather than silently
/// falling back to the English-US string via <see cref="Loc.Get"/>.
/// <para>
/// Comparing against the literal English-US value (not just <c>!= key</c>) is what makes this
/// strict: <see cref="Loc.Get"/> falls back to the English-US string, not the raw key, when a
/// language's table is missing an entry, so a weaker check would not catch a missing translation.
/// </para>
/// <para>
/// <see cref="Loc"/> holds its active language as static state shared across tests, so — like
/// <c>LocTests</c> — this restores <see cref="AppLanguage.EnglishUS"/> in a <c>finally</c>, and
/// belongs to <see cref="LocalizationCollection"/> so it never races <c>GreetingTests</c> or
/// <c>LocTests</c> over that same state.
/// </para>
/// </summary>
[Collection(LocalizationCollection.Name)]
public class SettingsLocalizationGroupATests
{
    private static readonly AppLanguage[] Languages =
    {
        AppLanguage.French,
        AppLanguage.German,
        AppLanguage.Spanish,
        AppLanguage.Italian,
        AppLanguage.PortugueseBR,
        AppLanguage.Dutch,
        AppLanguage.Polish,
        AppLanguage.Turkish,
    };

    /// <summary>Every key the Settings/Orbit Layout wiring added, in the same order as the
    /// "Orbit Layout screen" / "Settings screen" sections of <c>Loc.EnglishUS.cs</c>.</summary>
    private static readonly string[] Keys =
    {
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
    /// the English-US value — a genuine cognate or an already-established loanword in that
    /// language's own tech/OS vocabulary (matching how Windows itself localizes the term there),
    /// never an oversight:
    /// <list type="bullet">
    /// <item><c>Squircle</c> (an invented square+circle portmanteau with no native equivalent in
    /// any of these languages), the "{0} px" pixel value, and the "WinUI 3 • {0} • {1}" framework
    /// line carry no language-specific content to translate in the first place.</item>
    /// <item><c>Quadrant</c>, "Version", <c>Tint</c>, <c>Orbit</c> and the preposition "In" are true
    /// cognates/shared words in the languages listed below — there is no other native word for
    /// them.</item>
    /// <item><c>Support</c>, <c>Privacy</c>, <c>Updates</c>, <c>General</c> and <c>Zoom</c> are
    /// already-established loanwords in those languages' everyday tech/OS vocabulary.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<(AppLanguage Language, string Key)> KnownTies = new()
    {
        (AppLanguage.German, "OrbitLayout.HotCorner.Orbit"),
        (AppLanguage.Dutch, "OrbitLayout.HotCorner.Orbit"),
        (AppLanguage.French, "OrbitLayout.HotCorner.Quadrant"),
        (AppLanguage.German, "OrbitLayout.HotCorner.Quadrant"),
        (AppLanguage.Dutch, "OrbitLayout.HoverStyle.Tint"),
        (AppLanguage.French, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.German, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.Spanish, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.Italian, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.PortugueseBR, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.Dutch, "OrbitLayout.HoverStyle.Zoom"),
        (AppLanguage.Dutch, "OrbitLayout.HoverStyle.ZoomIn"),
        (AppLanguage.French, "OrbitLayout.ItemShape.Squircle"),
        (AppLanguage.German, "OrbitLayout.ItemShape.Squircle"),
        (AppLanguage.Spanish, "OrbitLayout.ItemShape.Squircle"),
        (AppLanguage.Italian, "OrbitLayout.ItemShape.Squircle"),
        (AppLanguage.PortugueseBR, "OrbitLayout.ItemShape.Squircle"),
        (AppLanguage.Dutch, "OrbitLayout.ItemShape.Squircle"),
        (AppLanguage.Polish, "OrbitLayout.ItemShape.Squircle"),
        (AppLanguage.Turkish, "OrbitLayout.ItemShape.Squircle"),
        (AppLanguage.French, "OrbitLayout.Radius.Value"),
        (AppLanguage.German, "OrbitLayout.Radius.Value"),
        (AppLanguage.Spanish, "OrbitLayout.Radius.Value"),
        (AppLanguage.Italian, "OrbitLayout.Radius.Value"),
        (AppLanguage.PortugueseBR, "OrbitLayout.Radius.Value"),
        (AppLanguage.Dutch, "OrbitLayout.Radius.Value"),
        (AppLanguage.Polish, "OrbitLayout.Radius.Value"),
        (AppLanguage.Turkish, "OrbitLayout.Radius.Value"),
        (AppLanguage.French, "Settings.About.Framework"),
        (AppLanguage.German, "Settings.About.Framework"),
        (AppLanguage.Spanish, "Settings.About.Framework"),
        (AppLanguage.Italian, "Settings.About.Framework"),
        (AppLanguage.PortugueseBR, "Settings.About.Framework"),
        (AppLanguage.Dutch, "Settings.About.Framework"),
        (AppLanguage.Polish, "Settings.About.Framework"),
        (AppLanguage.Turkish, "Settings.About.Framework"),
        (AppLanguage.French, "Settings.About.Version"),
        (AppLanguage.German, "Settings.About.Version"),
        (AppLanguage.German, "Settings.Feedback.Header"),
        (AppLanguage.Spanish, "Settings.General.Header"),
        (AppLanguage.Spanish, "Settings.Nav.General"),
        (AppLanguage.Italian, "Settings.Nav.Privacy"),
        (AppLanguage.Dutch, "Settings.Nav.Privacy"),
        (AppLanguage.German, "Settings.Nav.Support"),
        (AppLanguage.Italian, "Settings.Privacy.Header"),
        (AppLanguage.Dutch, "Settings.Privacy.Header"),
    };

    [Fact]
    public void Every_assigned_language_translates_every_newly_added_key()
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
