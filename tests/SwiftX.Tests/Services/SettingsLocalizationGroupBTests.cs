using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Covers the ~170 keys a teammate added to <see cref="Loc.EnglishUS"/> (and <c>EnglishUK</c>) when
/// wiring the Orbit Layout screen and Settings screen up to <see cref="Loc.Get"/> /
/// <see cref="Loc.Format"/> for real, instead of the hard-coded English those screens used before.
/// This class is scoped to the 7 languages assigned as "group B" for that follow-up translation
/// work: Russian, Arabic, Hebrew, Hindi, Japanese, Chinese (Simplified), and Korean. The other
/// languages (French, German, Spanish, Italian, Portuguese, Dutch, Polish, Turkish) were
/// translated separately and are out of scope here.
/// <para>
/// <see cref="Loc.Get"/> falls back to the English-US string — not the raw key — when a language's
/// table is missing an entry, so a bare <c>!= key</c> check would not catch a missing translation.
/// Comparing against the actual English-US value, as done below, is what actually proves a key was
/// translated rather than silently inherited.
/// </para>
/// <para>
/// <see cref="Loc"/> holds its active language as static state shared across tests, so (like
/// <c>LocTests</c>) this class restores <see cref="AppLanguage.EnglishUS"/> in a <c>finally</c>.
/// </para>
/// </summary>
[Collection(LocalizationCollection.Name)]
public class SettingsLocalizationGroupBTests
{
    private static readonly AppLanguage[] GroupBLanguages =
    {
        AppLanguage.Russian,
        AppLanguage.Arabic,
        AppLanguage.Hebrew,
        AppLanguage.Hindi,
        AppLanguage.Japanese,
        AppLanguage.ChineseSimplified,
        AppLanguage.Korean,
    };

    /// <summary>
    /// The ~170 keys added for the Orbit Layout screen and the Settings screen, in the same order
    /// they appear in <see cref="Loc.EnglishUS"/>.
    /// </summary>
    private static readonly string[] NewKeys =
    {
        // Orbit Layout screen
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

        // Settings screen
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
        // Settings.About.Framework is deliberately excluded: its English value, "WinUI 3  •  {0}  •
        // {1}", is a version/build string with no translatable words, so it is legitimately
        // identical across every language rather than a missed translation.
        "Settings.About.TermsAndConditions",
        "Settings.About.FeedbackRating.Title",
        "Settings.About.FeedbackRating.Body",
        "Settings.About.FeedbackRating.ShareFeedback",
        "Settings.About.FeedbackRating.RateOnStore",
        "Settings.About.FeedbackRating.StarOnGitHub",
    };

    [Fact]
    public void Every_group_B_language_translates_every_new_key()
    {
        try
        {
            Loc.Initialize(AppLanguage.EnglishUS);
            var englishValues = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var key in NewKeys)
                englishValues[key] = Loc.Get(key);

            foreach (var language in GroupBLanguages)
            {
                Loc.Initialize(language);
                Assert.Equal(language, Loc.CurrentLanguage);

                foreach (var key in NewKeys)
                {
                    var translated = Loc.Get(key);
                    Assert.NotEqual(englishValues[key], translated);
                }
            }
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }

    /// <summary>
    /// Spot-checks that every new key at least round-trips through <see cref="Loc.Format"/> without
    /// losing or renumbering its <c>{0}</c>/<c>{1}</c> placeholders, for the handful of new keys
    /// that carry one.
    /// </summary>
    [Theory]
    [InlineData(AppLanguage.Russian)]
    [InlineData(AppLanguage.Arabic)]
    [InlineData(AppLanguage.Hebrew)]
    [InlineData(AppLanguage.Hindi)]
    [InlineData(AppLanguage.Japanese)]
    [InlineData(AppLanguage.ChineseSimplified)]
    [InlineData(AppLanguage.Korean)]
    public void Templated_keys_substitute_their_arguments(AppLanguage language)
    {
        try
        {
            Loc.Initialize(language);

            Assert.Contains("7", Loc.Format("OrbitLayout.ItemSize.NameTemplate", "7"));
            Assert.Contains("42", Loc.Format("OrbitLayout.Radius.Value", 42));
            Assert.Contains("3", Loc.Format("Settings.About.Updates.Available", "3"));
            Assert.Contains("5", Loc.Format("Settings.General.Frost.NameTemplate", "5"));

            var freeSlots = Loc.Format("OrbitLayout.AddToOrbit.FreeSlots", 2);
            Assert.Contains("2", freeSlots);

            var replacing = Loc.Format("OrbitLayout.AddToOrbit.ReplacingSummary", "Notepad");
            Assert.Contains("Notepad", replacing);
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }
}
