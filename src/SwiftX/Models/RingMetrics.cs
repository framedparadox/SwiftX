namespace SwiftX.Models;

/// <summary>How large the items arranged around the ring are drawn.</summary>
public enum RingItemSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// Ring geometry. Two independent knobs, and only two:
/// <list type="bullet">
/// <item><see cref="ItemSize"/> — how big each item around the ring is drawn.</item>
/// <item><see cref="SetRadius"/> — how far out from the center they sit.</item>
/// </list>
/// <para>
/// The hub is deliberately not among them: it is a fixed small mark
/// (<see cref="Hub"/>/<see cref="HubLogo"/>) that no setting scales. An earlier "ring size"
/// density preset drove the hub and a radius default, which meant the only visible effect of
/// changing it was the center growing — the opposite of what the name promised, and invisible
/// entirely once an explicit radius was set. Item size replaces it.
/// </para>
/// </summary>
public static class RingMetrics
{
    public static RingItemSize ItemSize { get; private set; } = RingItemSize.Medium;
    private static double? s_radiusOverride;

    public static event Action? Changed;

    public static void SetItemSize(RingItemSize size)
    {
        if (ItemSize == size) return;
        ItemSize = size;
        Changed?.Invoke();
    }

    /// <summary>Explicit distance from the hub to each item's center. Null restores <see cref="DefaultRadius"/>.</summary>
    public static void SetRadius(double? radius)
    {
        if (s_radiusOverride == radius) return;
        s_radiusOverride = radius;
        Changed?.Invoke();
    }

    public const double MinRadius = 80;
    public const double MaxRadius = 220;
    public const double DefaultRadius = 100;

    public static double Radius => s_radiusOverride ?? DefaultRadius;

    public static double Bubble => ItemSize switch
    {
        RingItemSize.Small => 38,
        RingItemSize.Large => 64,
        _ => 50,
    };

    public static double Icon => ItemSize switch
    {
        RingItemSize.Small => 17,
        RingItemSize.Large => 28,
        _ => 22,
    };

    public static double Glyph => ItemSize switch
    {
        RingItemSize.Small => 13,
        RingItemSize.Large => 20,
        _ => 16,
    };

    /// <summary>Marker on an item that has children. Scales with the item so it never looks bolted on.</summary>
    public static double Bump => Math.Round(Bubble * 0.12);

    // ---- Hub: fixed, unaffected by every setting -------------------------------------------

    /// <summary>Layout box the centered logo occupies. Nothing paints behind it.</summary>
    public const double Hub = 72;

    public const double HubLogo = 28;

    public static double DeadZone => 36;

    /// <summary>How much an item grows under the pointer. Budgeted into <see cref="ContentRadius"/>.</summary>
    public const double HoverScale = 1.14;

    /// <summary>
    /// Close is the one item that moves the other way under the pointer: it shrinks, reading as
    /// pressed-in rather than offered-up, which sets it apart from every other item without
    /// competing with the red fill for attention.
    /// </summary>
    public const double CloseHoverScale = 0.75;

    // ---- Flyout (outer) ring -------------------------------------------------------------
    // A group expands into a concentric outer ring rather than replacing the inner one, so the
    // outer band has to clear the inner items even when one of them is hover-grown.

    /// <summary>Clear space between the outer edge of the inner ring and the inner edge of the flyout ring.</summary>
    public static double FlyoutGap => 16;

    public static double FlyoutBubble => Math.Round(Bubble * 0.88);

    /// <summary>Distance from the ring center to a flyout item's center.</summary>
    public static double FlyoutRadius =>
        Radius + (Bubble / 2) * HoverScale + FlyoutGap + FlyoutBubble / 2;

    public static double FlyoutIcon => Math.Round(Icon * 0.88);

    public static double FlyoutGlyph => Math.Round(Glyph * 0.9);

    /// <summary>Where the flyout band starts — pointer distances beyond this target the outer ring.</summary>
    public static double FlyoutBandStart => Radius + (Bubble / 2) * HoverScale + FlyoutGap / 2;

    /// <summary>
    /// Angular spacing between adjacent flyout items, chosen so their circles keep a constant
    /// pixel gap at the current <see cref="FlyoutRadius"/> instead of a fixed angle that would
    /// crowd at a small radius and scatter at a large one.
    /// </summary>
    public static double FlyoutStepDegrees
    {
        get
        {
            double chord = (FlyoutBubble + 12) / 2;
            double ratio = Math.Clamp(chord / FlyoutRadius, 0, 1);
            double step = 2 * Math.Asin(ratio) * 180.0 / Math.PI;
            return Math.Clamp(step, 14, 40);
        }
    }

    public const int FlyoutSlotCount = 8;

    // ---- Hover name pill -------------------------------------------------------------------

    /// <summary>Clear space between an item's hover-grown edge and the near edge of its name pill.</summary>
    public const double PillGap = 10;

    /// <summary>
    /// Pill text size. Deliberately below the ring's other type: the label is a transient
    /// confirmation of what is under the pointer, not something to be read at a glance from
    /// across the screen, and a large one crowds the ring it floats over.
    /// </summary>
    public const double PillFontSize = 10.5;

    /// <summary>
    /// Widest a pill may get before its text wraps. Bounded so one very long item name cannot
    /// stretch a pill clear across the screen; past this the text wraps onto another line, which
    /// still shows all of it.
    /// </summary>
    public const double PillMaxWidth = 220;

    // ---- Window / hit region -------------------------------------------------------------

    /// <summary>
    /// Radius of the ring itself at its largest — inner ring, hover growth, and an expanded
    /// flyout ring, with a little slack.
    /// <para>
    /// The window's hit region has to bound painted content, because
    /// <see cref="Services.WindowChrome.SetEllipticHitRegions"/> genuinely clips DWM's composited
    /// pixels; a region drawn tight to the resting items is what used to shave the edge off an
    /// item the moment hover grew it. The hover pill is deliberately <em>not</em> budgeted in
    /// here, though — a pill is as wide as someone's longest item name, and reserving that much
    /// in every direction would both swell the click-capturing disc and, because the ring center
    /// is clamped this far from every screen edge, stop the ring appearing under the cursor at
    /// all. RingWindow adds a second region circle around the pill only while one is showing.
    /// </para>
    /// </summary>
    public static double ContentRadius =>
        FlyoutRadius + (FlyoutBubble / 2) * HoverScale + 14;

    /// <summary>Diameter of the ring's full painted footprint.</summary>
    public static double WindowSize => ContentRadius * 2;

    public const int SlotCount = 8;

    /// <summary>
    /// Slots available to user items: the last two are reserved for the built-in Settings and
    /// Close entries, which are always on the ring (see <see cref="SystemRingItems"/>).
    /// </summary>
    public const int UserSlotCount = SlotCount - 2;

    /// <summary>
    /// The slot (0..<see cref="SlotCount"/>-1) angularly nearest a pointer at offset
    /// (<paramref name="dx"/>, <paramref name="dy"/>) from the ring center, matching the layout
    /// in RingWindow.CreateBubble: slot 0 is straight up, slots advance clockwise 45° apart.
    /// Pure geometry — no notion of which slots actually have an item in them, so callers must
    /// still check that one exists at the returned index.
    /// </summary>
    public static int NearestSlotIndex(double dx, double dy)
    {
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double normalized = (angle + 90 + 360) % 360;
        return (int)Math.Round(normalized / 45.0) % SlotCount;
    }

    /// <summary>Angle (radians, 0 = straight up, clockwise) of inner slot <paramref name="index"/>.</summary>
    public static double SlotAngleRad(int index) => (-90 + index * 45) * Math.PI / 180.0;
}
