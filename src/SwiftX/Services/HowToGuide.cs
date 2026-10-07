using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>Where a guide section's button takes the user.</summary>
public enum GuideAction
{
    None,

    /// <summary>The Orbit screen, where the dock is arranged.</summary>
    OpenOrbit,

    /// <summary>The Orbit screen with its settings sidebar showing, which is where the hot corner lives.</summary>
    OpenOrbitSettings,

    /// <summary>Settings, General, which is where the keyboard shortcut lives.</summary>
    OpenGeneralSettings,

    /// <summary>The Elements screen: everything on the dock, listed by type.</summary>
    OpenElements,
}

/// <summary>One topic of the Home screen's how-to guide.</summary>
/// <param name="Title">The topic, as the user would ask it.</param>
/// <param name="Glyph">Segoe Fluent Icons glyph drawn beside the title.</param>
/// <param name="Summary">One line that says where things stand right now (for example whether the
/// hot corner is on), so the topic starts from the user's own setup and not a generic one.</param>
/// <param name="Steps">The instructions, in order. Plain sentences: the guide numbers them.</param>
/// <param name="ActionLabel">Text of the button that takes the user to the place the topic is
/// about, or empty when <paramref name="Action"/> is <see cref="GuideAction.None"/>.</param>
public sealed record GuideSection(
    string Title,
    string Glyph,
    string Summary,
    IReadOnlyList<string> Steps,
    string ActionLabel,
    GuideAction Action);

/// <summary>
/// The content of the how-to guide that opens over the Home screen: setting up the dock, calling
/// it up, using the hot corner, and changing what is on it.
/// <para>
/// Built from the user's own configuration rather than written once as static text, so it names the
/// shortcut they actually assigned, the corner they actually chose, and whether either is switched on
/// at all. A guide that says "press Ctrl+Alt+Space" to someone who rebound it, or explains a hot
/// corner that is off without saying so, is worse than no guide. It is also kept apart from the
/// window that shows it, so those facts can be checked without one: every number in it is read from
/// the constant the app itself uses, so it cannot drift from what the app does.
/// </para>
/// </summary>
public static class HowToGuide
{
    /// <summary>The topics, in the order the guide shows them.</summary>
    public static IReadOnlyList<GuideSection> Sections(AppConfig config) =>
    [
        SetUpTheDock(),
        HowToInvoke(config),
        HotCornerGuide(config),
        ChangeItems(),
    ];

    private static GuideSection SetUpTheDock() => new(
        Title: "Setting up the dock",
        Glyph: char.ConvertFromUtf32(0xE713),
        Summary: $"The dock has {OrbitMetrics.UserSlotCount} places for your own items.",
        Steps:
        [
            $"Open Orbit from Home. The ring holds {OrbitMetrics.UserSlotCount} items of your own, and Settings and Close always stay at the end.",
            "On the Orbit screen, press the Elements button (the hexagon) to open Add to orbit: apps, shortcuts, folders, links, groups and tools.",
            "Click an entry to put it in the next free place, or drag it onto the ring. You can also drag a file, folder, link or app in from Explorer or the desktop.",
            "Press the gear on the same screen for item size, shape, hover style and the hot corner. The dock follows as you change them.",
        ],
        ActionLabel: "Open Orbit",
        Action: GuideAction.OpenOrbit);

    private static GuideSection HowToInvoke(AppConfig config)
    {
        string hotkey = HotkeyText(config);

        return new(
            Title: "How to invoke it",
            Glyph: char.ConvertFromUtf32(0xE765),
            Summary: config.HotkeyEnabled
                ? $"Your shortcut is {hotkey}."
                : "The keyboard shortcut is switched off.",
            Steps:
            [
                config.HotkeyEnabled
                    ? $"Press {hotkey} from anywhere to show the dock at the pointer, and press it again to hide it."
                    : "Turn the keyboard shortcut on in Settings, General, and choose the keys you want.",
                "Click the SwiftX icon in the system tray to show or hide the dock. Right-click it for the tray menu: Show or Hide, Add new, Settings and Quit.",
                config.HotCornerEnabled
                    ? $"Or push the pointer into the {CornerName(config.HotCorner)} corner of the screen (see the hot corner topic below)."
                    : "Or switch the hot corner on and push the pointer into a screen corner (see the hot corner topic below).",
                "Click an item to launch it. Hover a group to open its own ring. Press Esc, or click anywhere else, to close the dock.",
            ],
            ActionLabel: "Open Settings",
            Action: GuideAction.OpenGeneralSettings);
    }

    private static GuideSection HotCornerGuide(AppConfig config)
    {
        string corner = CornerName(config.HotCorner);
        string layout = config.HotCornerLayout == HotCornerLayout.Quadrant
            ? $"a quarter circle of {QuadrantLayout.ItemCount} items"
            : "the whole ring";

        return new(
            Title: "How to use the hot corner",
            Glyph: char.ConvertFromUtf32(0xE740),
            Summary: config.HotCornerEnabled
                ? $"On: the {corner} corner, showing {layout}."
                : "Off. Switch it on from the Orbit screen's settings.",
            Steps:
            [
                $"Push the pointer into the {corner} corner of the screen and hold it there for about a quarter of a second. The trigger is a tiny patch right at the corner.",
                $"Layout decides what appears. Orbit shows the whole ring. Quadrant fans out {QuadrantLayout.ItemCount} items around the corner, so each one is a short flick away.",
                $"In the Quadrant layout you arrange those {QuadrantLayout.ItemCount} items yourself, in the corner preview on the Orbit screen.",
                "To switch the hot corner on or off, or to pick another corner or layout: Orbit screen, gear button, Hot corner.",
            ],
            ActionLabel: "Open hot corner settings",
            Action: GuideAction.OpenOrbitSettings);
    }

    private static GuideSection ChangeItems() => new(
        Title: "How to change items",
        Glyph: char.ConvertFromUtf32(0xE70F),
        Summary: "Everything is done from the Orbit screen or the Elements screen.",
        Steps:
        [
            "Reorder: on the Orbit screen, drag an item around the ring to a new place.",
            "Replace: right-click an item and choose Replace, or click it and use Replace on its card. Then pick something from the Add to orbit list, or drop it onto the ring.",
            "Remove: right-click an item and choose Delete.",
            "Icon: click an item, then click the icon on its card to choose a different one, or a different colour for it. Right-click a group and choose Edit icon to do the same.",
            "Elements screen: press the Elements button on Home to see everything by type. Show or hide an item, edit it or delete it there.",
            "Groups: choose New group in Add to orbit to make one, click a group in the list to see what is inside it, and drop items onto it to put them inside. Hover a group on the dock to open its ring.",
        ],
        ActionLabel: "Open Elements",
        Action: GuideAction.OpenElements);

    /// <summary>
    /// The shortcut as the user typed it, normalised through <see cref="HotkeyGesture"/> when it
    /// parses and shown as stored when it does not, so a hand-edited value still reads as something
    /// rather than vanishing. An empty one falls back to the default, which is what the app itself
    /// registers in that case.
    /// </summary>
    public static string HotkeyText(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Hotkey))
            return HotkeyGesture.Default.ToString();

        return HotkeyGesture.TryParse(config.Hotkey, out var gesture)
            ? gesture.ToString()
            : config.Hotkey.Trim();
    }

    /// <summary>A corner as a person says it: "top-left".</summary>
    public static string CornerName(HotCorner corner) => corner switch
    {
        HotCorner.TopLeft => "top-left",
        HotCorner.TopRight => "top-right",
        HotCorner.BottomLeft => "bottom-left",
        HotCorner.BottomRight => "bottom-right",
        _ => corner.ToString(),
    };
}
