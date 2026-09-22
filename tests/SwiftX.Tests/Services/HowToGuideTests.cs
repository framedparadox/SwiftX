using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The how-to guide that slides out of the Home screen. Its point is to describe the user's own
/// setup, so what is guarded is that: the shortcut and corner it names are the ones configured, a
/// switched-off feature is said to be off rather than explained as though it worked, and every number
/// it quotes is read from the constant the app itself uses.
/// </summary>
public class HowToGuideTests
{
    private static IReadOnlyList<GuideSection> Guide(AppConfig? config = null) =>
        HowToGuide.Sections(config ?? new AppConfig());

    private static GuideSection Section(string titleContains, AppConfig? config = null) =>
        Guide(config).Single(s => s.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));

    private static string AllText(GuideSection section) =>
        string.Join('\n', section.Steps.Append(section.Summary));

    // ---- Shape -------------------------------------------------------------------------------

    /// <summary>The four topics that were asked for, in that order.</summary>
    [Fact]
    public void The_guide_covers_the_dock_invoking_it_the_hot_corner_and_changing_items()
    {
        var titles = Guide().Select(s => s.Title).ToArray();

        Assert.Equal(4, titles.Length);
        Assert.Contains("dock", titles[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invoke", titles[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hot corner", titles[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("change items", titles[3], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_section_has_a_summary_and_several_steps()
    {
        foreach (var section in Guide())
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Summary), section.Title);
            Assert.True(section.Steps.Count >= 3, $"{section.Title} has {section.Steps.Count} steps.");
            Assert.All(section.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step)));
        }
    }

    [Fact]
    public void Every_section_that_offers_a_button_labels_it_and_every_label_has_a_button()
    {
        foreach (var section in Guide())
        {
            if (section.Action == GuideAction.None)
                Assert.True(string.IsNullOrEmpty(section.ActionLabel), section.Title);
            else
                Assert.False(string.IsNullOrWhiteSpace(section.ActionLabel), section.Title);
        }
    }

    [Fact]
    public void The_sections_go_to_four_different_places()
    {
        var actions = Guide().Select(s => s.Action).ToArray();

        Assert.Equal(actions.Length, actions.Distinct().Count());
        Assert.DoesNotContain(GuideAction.None, actions);
    }

    /// <summary>
    /// A glyph is drawn in the icon font, where a private-use codepoint is expected and anything
    /// else is a blank box. Each has to be exactly one such character.
    /// </summary>
    [Fact]
    public void Every_glyph_is_a_single_private_use_icon()
    {
        foreach (var section in Guide())
        {
            Assert.Single(section.Glyph);
            Assert.InRange((int)section.Glyph[0], 0xE000, 0xF8FF);
        }
    }

    [Fact]
    public void The_glyphs_tell_the_sections_apart()
    {
        var glyphs = Guide().Select(s => s.Glyph).ToArray();

        Assert.Equal(glyphs.Length, glyphs.Distinct().Count());
    }

    // ---- The keyboard shortcut ---------------------------------------------------------------------

    [Fact]
    public void The_shortcut_named_is_the_one_configured()
    {
        var config = new AppConfig { Hotkey = "Ctrl+Shift+K", HotkeyEnabled = true };

        var invoke = Section("invoke", config);

        Assert.Contains("Ctrl+Shift+K", AllText(invoke));
        Assert.Equal(GuideAction.OpenGeneralSettings, invoke.Action);
    }

    /// <summary>Spacing and casing in a hand-edited value are tidied the way the app tidies them.</summary>
    [Fact]
    public void A_shortcut_written_loosely_is_shown_in_its_normal_form()
    {
        var config = new AppConfig { Hotkey = "control + alt + k", HotkeyEnabled = true };

        Assert.Equal(
            HotkeyGesture.TryParse("control + alt + k", out var parsed) ? parsed.ToString() : "?",
            HowToGuide.HotkeyText(config));
        Assert.Contains(HowToGuide.HotkeyText(config), AllText(Section("invoke", config)));
    }

    [Fact]
    public void A_shortcut_that_does_not_parse_is_shown_as_stored_and_an_empty_one_as_the_default()
    {
        Assert.Equal("Hyper+Q", HowToGuide.HotkeyText(new AppConfig { Hotkey = "  Hyper+Q " }));
        Assert.Equal(HotkeyGesture.Default.ToString(), HowToGuide.HotkeyText(new AppConfig { Hotkey = "" }));
        Assert.Equal(HotkeyGesture.Default.ToString(), HowToGuide.HotkeyText(new AppConfig { Hotkey = "   " }));
    }

    /// <summary>
    /// Telling someone to press a shortcut that is switched off is the failure this guide exists to
    /// avoid. It has to say it is off, and how to turn it on, and not name the keys as a way in.
    /// </summary>
    [Fact]
    public void A_disabled_shortcut_is_said_to_be_off_and_is_not_offered_as_a_way_in()
    {
        var config = new AppConfig { Hotkey = "Ctrl+Shift+K", HotkeyEnabled = false };

        var invoke = Section("invoke", config);

        Assert.Contains("off", invoke.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ctrl+Shift+K", AllText(invoke));
        Assert.Contains("Turn the keyboard shortcut on", invoke.Steps[0]);
    }

    [Fact]
    public void Invoking_also_covers_the_tray_icon_and_how_to_dismiss()
    {
        var text = AllText(Section("invoke"));

        Assert.Contains("tray", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Esc", text);
    }

    // ---- The hot corner --------------------------------------------------------------------------

    [Theory]
    [InlineData(HotCorner.TopLeft, "top-left")]
    [InlineData(HotCorner.TopRight, "top-right")]
    [InlineData(HotCorner.BottomLeft, "bottom-left")]
    [InlineData(HotCorner.BottomRight, "bottom-right")]
    public void The_corner_named_is_the_one_configured(HotCorner corner, string name)
    {
        var config = new AppConfig { HotCorner = corner, HotCornerEnabled = true };

        Assert.Equal(name, HowToGuide.CornerName(corner));
        Assert.Contains(name, Section("hot corner", config).Summary);
        Assert.Contains(name, Section("hot corner", config).Steps[0]);
        Assert.Contains(name, AllText(Section("invoke", config)));
    }

    [Fact]
    public void Every_corner_has_a_name_of_its_own()
    {
        var names = Enum.GetValues<HotCorner>().Select(HowToGuide.CornerName).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(names, name => Assert.Contains('-', name));
    }

    [Fact]
    public void A_disabled_hot_corner_is_said_to_be_off()
    {
        var config = new AppConfig { HotCorner = HotCorner.BottomRight, HotCornerEnabled = false };

        var summary = Section("hot corner", config).Summary;

        Assert.StartsWith("Off", summary);
        Assert.DoesNotContain("bottom-right", summary);
        Assert.Contains("switch the hot corner on", AllText(Section("invoke", config)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_summary_says_which_layout_the_corner_shows()
    {
        var orbit = Section("hot corner", new AppConfig { HotCornerEnabled = true, HotCornerLayout = HotCornerLayout.Orbit });
        var quadrant = Section("hot corner", new AppConfig { HotCornerEnabled = true, HotCornerLayout = HotCornerLayout.Quadrant });

        Assert.Contains("whole ring", orbit.Summary);
        Assert.Contains("quarter circle", quadrant.Summary);
        Assert.Contains($"{QuadrantLayout.ItemCount} items", quadrant.Summary);
    }

    /// <summary>The hold time is quoted as "about a quarter of a second"; that has to stay true.</summary>
    [Fact]
    public void The_hold_time_it_quotes_is_the_one_the_watcher_uses()
    {
        Assert.Equal(250, HotCornerWatcher.DwellMilliseconds);
        Assert.Contains("quarter of a second", Section("hot corner").Steps[0]);
    }

    // ---- The numbers it quotes ---------------------------------------------------------------------

    [Fact]
    public void The_dock_topic_quotes_the_real_number_of_places()
    {
        var dock = Section("dock");

        Assert.Contains(OrbitMetrics.UserSlotCount.ToString(), dock.Summary);
        Assert.Contains(OrbitMetrics.UserSlotCount.ToString(), dock.Steps[0]);
        Assert.Equal(GuideAction.OpenOrbit, dock.Action);
    }

    [Fact]
    public void The_hot_corner_topic_quotes_the_real_quadrant_size()
    {
        var text = AllText(Section("hot corner"));

        Assert.Contains($"{QuadrantLayout.ItemCount} items", text);
        Assert.Equal(GuideAction.OpenOrbitSettings, Section("hot corner").Action);
    }

    // ---- Changing items ------------------------------------------------------------------------------

    /// <summary>
    /// The right-click menu offers Replace and Delete, and the guide names them by those words, so a
    /// user can find the entry it describes.
    /// </summary>
    [Fact]
    public void Changing_items_names_the_menu_entries_the_app_actually_has()
    {
        var change = Section("change items");
        var text = AllText(change);

        Assert.Contains("Replace", text);
        Assert.Contains("Delete", text);
        Assert.Contains("Elements", text);
        Assert.Contains("drag", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GuideAction.OpenElements, change.Action);
    }

    // ---- Plain text ---------------------------------------------------------------------------------

    /// <summary>
    /// Everything but the glyphs is plain ASCII: the guide is read on screens set to any language and
    /// font, and a typographic dash or an ellipsis character is where a fallback font shows through.
    /// </summary>
    [Fact]
    public void The_text_is_plain_ascii()
    {
        foreach (var section in Guide())
        {
            var text = section.Title + AllText(section) + section.ActionLabel;
            Assert.All(text, c => Assert.True(c < 128, $"'{c}' (U+{(int)c:X4}) in \"{section.Title}\"."));
        }
    }
}
