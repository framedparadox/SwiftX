namespace SwiftX.Models;

/// <summary>
/// The orbit's built-in, non-scannable entries: Home, Settings and Close, which the "Add to
/// orbit" sidebar's Controls section offers like any other suggestion, and the one truly pinned
/// slot that is never anything else — the orbit logo, always last, always present, never
/// reorderable, removable, or offered for adding (see <see cref="CreateOrbitLogo"/>).
/// <para>
/// Home, Settings and Close used to be synthesized at render time rather than stored, the same way
/// the logo still is — see <see cref="Strip"/>'s own history below. That stopped being true the
/// moment they became optional: an item the user can choose to add has to be stored somewhere to
/// remember the choice, and <see cref="OrbitProfile.Items"/> is that somewhere, exactly like any
/// other orbit item. Only the logo is still synthesized, because it is never anything the user
/// picked — there is nothing to remember.
/// </para>
/// </summary>
public static class SystemOrbitItems
{
    public const string SettingsTarget = "swiftx:settings";
    public const string CloseTarget = "swiftx:close";
    public const string HomeTarget = "swiftx:home";

    /// <summary>The one slot that is always pinned — see <see cref="CreateOrbitLogo"/>.</summary>
    public const string OrbitLogoTarget = "swiftx:orbit-logo";

    /// <summary>Placeholder painted onto an empty user slot — see <see cref="CreateAddNew"/>.</summary>
    public const string AddNewTarget = "swiftx:quickadd";

    // The other default system operations (see CreateMute and below): single, well-known Windows
    // actions with nothing to configure, dispatched by OrbitManager.HandleItemActivation exactly
    // the way Settings/Home/Close already are. "Map keyboard action" is the one exception — it has
    // no target of its own here, because it is a real OrbitItemKind (KeyboardAction) rather than a
    // fixed swiftx: target, precisely so more than one can exist with a combination of its own; see
    // CreateKeyboardAction.
    public const string MuteTarget = "swiftx:mute";
    public const string LockTarget = "swiftx:lock";
    public const string SleepTarget = "swiftx:sleep";
    public const string ScreenshotTarget = "swiftx:screenshot";
    public const string EmptyRecycleBinTarget = "swiftx:empty-recycle-bin";
    public const string TaskManagerTarget = "swiftx:task-manager";

    // Built from the codepoint, not a literal character in source, so a glyph can't be silently
    // swapped for a different (or invisible) private-use character by a stray copy/paste. See the
    // memory note on this exact failure mode.
    private static readonly string SettingsGlyph = char.ConvertFromUtf32(0xE713);
    private static readonly string CloseGlyph = char.ConvertFromUtf32(0xE711);
    private static readonly string HomeGlyph = char.ConvertFromUtf32(0xE80F);
    private static readonly string AddNewGlyph = char.ConvertFromUtf32(0xE710);
    private static readonly string MuteGlyph = char.ConvertFromUtf32(0xE74F);
    private static readonly string LockGlyph = char.ConvertFromUtf32(0xE72E);
    private static readonly string SleepGlyph = char.ConvertFromUtf32(0xE7E8);
    private static readonly string ScreenshotGlyph = char.ConvertFromUtf32(0xE722);
    private static readonly string EmptyRecycleBinGlyph = char.ConvertFromUtf32(0xE74D);
    private static readonly string TaskManagerGlyph = char.ConvertFromUtf32(0xE9D9);
    private static readonly string KeyboardActionGlyph = char.ConvertFromUtf32(0xE765);

    /// <summary>
    /// Opens the app window on its Settings screen — see <see cref="OrbitManager.HandleItemActivation"/>.
    /// Offered from the sidebar's Controls section rather than pinned: the orbit's one fixed slot is
    /// <see cref="CreateOrbitLogo"/> now, which opens Orbit settings specifically.
    /// </summary>
    public static OrbitItem CreateSettings() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Settings",
        Target = SettingsTarget,
        CustomGlyph = SettingsGlyph,
        IsSystem = true,
    };

    /// <summary>Dismisses the live orbit (see <see cref="OrbitWindow.OnSelectAsync"/>'s own
    /// IsCloseItem check) — purely a target match, so this works the same whichever slot the item
    /// ends up placed in, pinned or not.</summary>
    public static OrbitItem CreateClose() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Close",
        Target = CloseTarget,
        CustomGlyph = CloseGlyph,
        IsSystem = true,
    };

    /// <summary>Opens the app window on its Home screen.</summary>
    public static OrbitItem CreateHome() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Home",
        Target = HomeTarget,
        CustomGlyph = HomeGlyph,
        IsSystem = true,
    };

    /// <summary>
    /// The orbit's one truly fixed slot, always last, standing in for Settings' old pinned position
    /// — except it opens Orbit settings specifically rather than the app window's default screen,
    /// and is drawn as the SwiftX mark itself rather than a glyph (see
    /// <see cref="ShellWindow.RenderPreview"/> and <see cref="OrbitWindow.CreateBubble"/>, both of
    /// which special-case <see cref="OrbitLogoTarget"/> to swap in <see cref="Services.AppLogo"/>).
    /// The glyph set here is only a fallback for any generic code that reads <see cref="OrbitItem.Glyph"/>
    /// without knowing about that substitution.
    /// </summary>
    public static OrbitItem CreateOrbitLogo() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Configure Orbit",
        Target = OrbitLogoTarget,
        CustomGlyph = SettingsGlyph,
        IsSystem = true,
    };

    /// <summary>Opens the same "Add to orbit" window as the tray icon's Add entry, targeting the
    /// same list (<c>Config.Primary.Items</c> — Settings › Elements). That window
    /// itself now accepts a drag-and-drop, so this is also the entry point for "bring something in
    /// from outside" — see AddItemWindow's own drop handling. Empty user slots on the live orbit
    /// use this as a placeholder.</summary>
    public static OrbitItem CreateAddNew() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Add new",
        Target = AddNewTarget,
        CustomGlyph = AddNewGlyph,
        IsSystem = true,
    };

    /// <summary>Toggles the system's mute state — see <see cref="Services.SystemActions.ToggleMute"/>.</summary>
    public static OrbitItem CreateMute() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Mute / Unmute",
        Target = MuteTarget,
        CustomGlyph = MuteGlyph,
        IsSystem = true,
    };

    /// <summary>Locks the session — see <see cref="Services.SystemActions.LockWorkstation"/>.</summary>
    public static OrbitItem CreateLock() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Lock",
        Target = LockTarget,
        CustomGlyph = LockGlyph,
        IsSystem = true,
    };

    /// <summary>Puts the machine to sleep — see <see cref="Services.SystemActions.Sleep"/>.</summary>
    public static OrbitItem CreateSleep() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Sleep",
        Target = SleepTarget,
        CustomGlyph = SleepGlyph,
        IsSystem = true,
    };

    /// <summary>Captures the screen to the clipboard — see
    /// <see cref="Services.SystemActions.CaptureScreenshot"/>.</summary>
    public static OrbitItem CreateScreenshot() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Screenshot",
        Target = ScreenshotTarget,
        CustomGlyph = ScreenshotGlyph,
        IsSystem = true,
    };

    /// <summary>Empties the Recycle Bin with no confirmation prompt — see
    /// <see cref="Services.SystemActions.EmptyRecycleBin"/>.</summary>
    public static OrbitItem CreateEmptyRecycleBin() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Empty Recycle Bin",
        Target = EmptyRecycleBinTarget,
        CustomGlyph = EmptyRecycleBinGlyph,
        IsSystem = true,
    };

    /// <summary>Opens Task Manager — see <see cref="Services.SystemActions.OpenTaskManager"/>.</summary>
    public static OrbitItem CreateTaskManager() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Task Manager",
        Target = TaskManagerTarget,
        CustomGlyph = TaskManagerGlyph,
        IsSystem = true,
    };

    /// <summary>
    /// A fresh, unconfigured "Map keyboard action" template, as the sidebar's own row builds it
    /// before it ever has a combination — see <see cref="Models.OrbitItemKind.KeyboardAction"/>'s
    /// own remarks on why this is a real kind rather than another fixed target. Not <c>IsSystem</c>:
    /// unlike the other items on this page, a configured instance is the user's own content, each
    /// with a combination and a name of its own, and has to survive a save/reload the way any
    /// other orbit item does — <c>IsSystem</c> is never persisted (see the class doc comment) and
    /// would otherwise make a reloaded instance drop out of whatever made it special.
    /// </summary>
    public static OrbitItem CreateKeyboardAction(string? displayName = null, string? gesture = null) => new()
    {
        Kind = OrbitItemKind.KeyboardAction,
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Map Keyboard Action" : displayName.Trim(),
        Arguments = gesture,
        CustomGlyph = KeyboardActionGlyph,
    };

    /// <summary>
    /// Every non-scannable built-in target, Home/Settings/Close included — used to keep all of them
    /// out of listings built for the user's own content (<see cref="ElementLibrary.OnOrbit"/>,
    /// <see cref="HotCornerItems"/>'s own group checks), which is a different question from whether
    /// a given one can be stored on the orbit at all (see <see cref="Strip"/>).
    /// </summary>
    public static bool IsSystemTarget(string? target) =>
        string.Equals(target, SettingsTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, CloseTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, HomeTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, OrbitLogoTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, AddNewTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, MuteTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, LockTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, SleepTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, ScreenshotTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, EmptyRecycleBinTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, TaskManagerTarget, StringComparison.OrdinalIgnoreCase);
    // KeyboardAction is deliberately not here: it has no fixed target (see CreateKeyboardAction),
    // and each configured instance is the user's own content, not a built-in to strip out of
    // listings the way the fixed swiftx: targets above are.

    /// <summary>
    /// Removes any stored orbit-logo entries from <paramref name="items"/> and every nested group.
    /// The logo is the only target here that can never legitimately be stored — it is synthesized
    /// onto the last slot at render time and offered nowhere a user could have added it — so a
    /// stored one can only be leftover state a future build should not render twice. Home, Settings
    /// and Close are deliberately left alone: those are optional now, added from the sidebar's
    /// Controls section like any other item, and a stored one is the user's own choice, not stale
    /// state to repair away. Returns true if anything was removed, so the caller can persist the
    /// migration immediately.
    /// </summary>
    public static bool Strip(List<OrbitItem> items)
    {
        bool changed = items.RemoveAll(i =>
            string.Equals(i.Target, OrbitLogoTarget, StringComparison.OrdinalIgnoreCase)) > 0;
        foreach (var group in items.Where(i => i.IsGroup))
            changed |= Strip(group.Children);
        return changed;
    }
}
