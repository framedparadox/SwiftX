namespace SwiftX.Models;

/// <summary>How large the items arranged around the orbit are drawn.</summary>
public enum OrbitItemSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// The outline an orbit item is drawn with — a separate axis from <see cref="OrbitItemSize"/>,
/// which says how much room an item takes rather than what fills it.
/// <para>
/// Persisted by name (see <see cref="AppConfig.ItemShape"/> and <c>OrbitStore</c>'s
/// converters), so these member names are part of the on-disk format and
/// cannot be renamed without stranding every config that already carries one.
/// </para>
/// </summary>
public enum OrbitItemShape
{
    /// <summary>A full disc — what every build before this one drew, and still what a fresh config gets.</summary>
    Circle,

    /// <summary>
    /// A square whose corners are rounded far enough to read as soft without reading as round.
    /// Named for the shape rather than described as one: "square with rounded edges" is a sentence,
    /// and this is a value that has to survive being written into a config file.
    /// </summary>
    Squircle,

    /// <summary>
    /// A plain square. The corners are left genuinely sharp rather than nearly so — a square with
    /// two pixels taken off its corners is just <see cref="Squircle"/> with a smaller number, and
    /// then there is no shape on the list that is actually square.
    /// </summary>
    Square,

    /// <summary>
    /// A pointy-top hexagon with its vertices rounded off, so the outline reads as a hexagon
    /// without the sharp corners a straight-edged one would put on a small bubble.
    /// </summary>
    Hexagon,
}

/// <summary>
/// Orbit geometry. Two independent knobs, and only two:
/// <list type="bullet">
/// <item><see cref="ItemSize"/> — how big each item around the orbit is drawn.</item>
/// <item><see cref="SetRadius"/> — how far out from the center they sit.</item>
/// </list>
/// <para>
/// The hub is deliberately not among them: it is a fixed small mark
/// (<see cref="Hub"/>/<see cref="HubLogo"/>) that no setting scales. An earlier "orbit size"
/// density preset drove the hub and a radius default, which meant the only visible effect of
/// changing it was the center growing — the opposite of what the name promised, and invisible
/// entirely once an explicit radius was set. Item size replaces it.
/// </para>
/// </summary>
public static class OrbitMetrics
{
    public static OrbitItemSize ItemSize { get; private set; } = OrbitItemSize.Medium;
    public static OrbitItemShape ItemShape { get; private set; } = OrbitItemShape.Circle;
    private static double? s_radiusOverride;

    public static event Action? Changed;

    public static void SetItemSize(OrbitItemSize size)
    {
        if (ItemSize == size) return;
        ItemSize = size;
        Changed?.Invoke();
    }

    /// <summary>
    /// The outline every bubble is drawn with. Lives here rather than being read out of
    /// <see cref="AppConfig"/> at each draw site for the same reason <see cref="ItemSize"/> does:
    /// the orbit, the hot-corner quadrant and the Orbit screen's preview all draw bubbles, and one
    /// of them (the orbit) has no config reference of its own to read.
    /// </summary>
    public static void SetItemShape(OrbitItemShape shape)
    {
        if (ItemShape == shape) return;
        ItemShape = shape;
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
        OrbitItemSize.Small => 38,
        OrbitItemSize.Large => 64,
        _ => 50,
    };

    public static double Icon => ItemSize switch
    {
        OrbitItemSize.Small => 17,
        OrbitItemSize.Large => 28,
        _ => 22,
    };

    public static double Glyph => ItemSize switch
    {
        OrbitItemSize.Small => 13,
        OrbitItemSize.Large => 20,
        _ => 16,
    };

    /// <summary>
    /// The corner radius a bubble <paramref name="size"/> across is drawn with under the current
    /// <see cref="ItemShape"/> — the single place that turns the shape setting into geometry, so
    /// the orbit, the flyout band, the corner quadrant and the preview cannot drift into drawing
    /// three different shapes from one setting.
    /// <para>
    /// Expressed as a fraction of the size rather than a constant because every one of those
    /// callers draws at a different diameter — the preview scales the whole orbit down, the flyout
    /// band runs at 88% of the inner one — and a fixed 14px corner would read as a barely-nicked
    /// square on a large bubble and as a circle on a small one. <see cref="OrbitItemShape.Circle"/>
    /// falls out of the same expression as half the size, which is the value every call site used
    /// to hard-code.
    /// </para>
    /// </summary>
    public static double CornerRadiusFor(double size) => ItemShape switch
    {
        OrbitItemShape.Squircle => Math.Round(size * 0.28),
        OrbitItemShape.Square => 0,
        OrbitItemShape.Hexagon => 0,
        _ => size / 2,
    };

    // ---- Hub: fixed, unaffected by every setting -------------------------------------------

    /// <summary>Layout box the centered logo occupies. Nothing paints behind it.</summary>
    public const double Hub = 72;

    public const double HubLogo = 28;

    public static double DeadZone => 36;

    /// <summary>
    /// How much an item grows under the pointer. Budgeted into <see cref="ContentRadius"/>, which is
    /// why <see cref="OrbitHoverStyle.Invert"/> and Zoom In share it: the layout leaves room for
    /// exactly this much growth, and no hover style may ask for more.
    /// </summary>
    public const double HoverScale = 1.14;

    /// <summary>
    /// How much an item shrinks under the pointer when <see cref="OrbitHoverStyle.Zoom"/> is set
    /// to <see cref="HoverZoomDirection.Out"/>: for someone who wants hover to read as a recede
    /// rather than an offer.
    /// </summary>
    public const double ZoomOutHoverScale = 0.92;

    /// <summary>
    /// The scale an item settles at while under the pointer, for the configured hover style.
    /// <para>
    /// <paramref name="restScale"/> is what the item rests at when nothing is hovering it, which is
    /// not always 1: a group whose flyout is open rests a little enlarged so that it reads as
    /// active. It only matters to <see cref="OrbitHoverStyle.Tint"/> and
    /// <see cref="OrbitHoverStyle.Animate"/>, whose whole contract is that hover leaves the settled
    /// size alone (Animate's own motion moves the item about that size); anything else names its own
    /// target size outright.
    /// </para>
    /// </summary>
    public static double HoverScaleFor(
        OrbitHoverStyle style, HoverZoomDirection direction, double restScale = 1) => style switch
        {
            OrbitHoverStyle.Zoom => direction == HoverZoomDirection.Out ? ZoomOutHoverScale : HoverScale,
            OrbitHoverStyle.Tint => restScale,
            OrbitHoverStyle.Animate => restScale,
            _ => HoverScale,
        };

    /// <summary>
    /// Close is the one item that moves the other way under the pointer: it shrinks, reading as
    /// pressed-in rather than offered-up, which sets it apart from every other item without
    /// competing with the red fill for attention.
    /// </summary>
    public const double CloseHoverScale = 0.75;

    // ---- Flyout (outer) orbit -------------------------------------------------------------
    // A group expands into a concentric outer orbit rather than replacing the inner one, so the
    // outer band has to clear the inner items even when one of them is hover-grown.

    /// <summary>Clear space between the outer edge of the inner orbit and the inner edge of the flyout orbit.</summary>
    public static double FlyoutGap => 16;

    public static double FlyoutBubble => Math.Round(Bubble * 0.88);

    /// <summary>Distance from the orbit center to a flyout item's center.</summary>
    public static double FlyoutRadius => FlyoutRadiusFor(Radius);

    /// <summary>
    /// <see cref="FlyoutRadius"/> for an inner orbit of an arbitrary radius. The quarter-circle a
    /// hot corner can summon sits further out than <see cref="Radius"/> (it has to clear two
    /// screen edges — see <see cref="QuadrantLayout"/>), and its flyout has to clear it in turn,
    /// so every band below is expressed against whatever radius the inner items actually got.
    /// </summary>
    public static double FlyoutRadiusFor(double radius) =>
        radius + (Bubble / 2) * HoverScale + FlyoutGap + FlyoutBubble / 2;

    public static double FlyoutIcon => Math.Round(Icon * 0.88);

    public static double FlyoutGlyph => Math.Round(Glyph * 0.9);

    /// <summary>Where the flyout band starts — pointer distances beyond this target the outer orbit.</summary>
    public static double FlyoutBandStart => FlyoutBandStartFor(Radius);

    /// <summary><see cref="FlyoutBandStart"/> for an inner orbit of an arbitrary radius.</summary>
    public static double FlyoutBandStartFor(double radius) =>
        radius + (Bubble / 2) * HoverScale + FlyoutGap / 2;

    /// <summary>
    /// Angular spacing between adjacent flyout items, chosen so their circles keep a constant
    /// pixel gap at the current <see cref="FlyoutRadius"/> instead of a fixed angle that would
    /// crowd at a small radius and scatter at a large one.
    /// </summary>
    public static double FlyoutStepDegrees => FlyoutStepDegreesFor(FlyoutRadius);

    /// <summary><see cref="FlyoutStepDegrees"/> at an arbitrary flyout radius.</summary>
    public static double FlyoutStepDegreesFor(double flyoutRadius)
    {
        double chord = (FlyoutBubble + 12) / 2;
        double ratio = Math.Clamp(chord / Math.Max(1, flyoutRadius), 0, 1);
        double step = 2 * Math.Asin(ratio) * 180.0 / Math.PI;
        return Math.Clamp(step, 14, 40);
    }

    public const int FlyoutSlotCount = 8;

    // ---- Hover name pill -------------------------------------------------------------------

    /// <summary>Clear space between an item's hover-grown edge and the near edge of its name pill.</summary>
    public const double PillGap = 10;

    /// <summary>
    /// Pill text size. Deliberately below the orbit's other type: the label is a transient
    /// confirmation of what is under the pointer, not something to be read at a glance from
    /// across the screen, and a large one crowds the orbit it floats over.
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
    /// Radius of the orbit itself at its largest — inner orbit, hover growth, and an expanded
    /// flyout orbit, with a little slack.
    /// <para>
    /// The window's hit region has to bound painted content, because
    /// <see cref="Services.WindowChrome.SetEllipticHitRegions"/> genuinely clips DWM's composited
    /// pixels; a region drawn tight to the resting items is what used to shave the edge off an
    /// item the moment hover grew it. The hover pill is deliberately <em>not</em> budgeted in
    /// here, though — a pill is as wide as someone's longest item name, and reserving that much
    /// in every direction would both swell the click-capturing disc and, because the orbit center
    /// is clamped this far from every screen edge, stop the orbit appearing under the cursor at
    /// all. OrbitWindow adds a second region circle around the pill only while one is showing.
    /// </para>
    /// </summary>
    public static double ContentRadius => ContentRadiusFor(Radius);

    /// <summary><see cref="ContentRadius"/> for an inner orbit of an arbitrary radius.</summary>
    public static double ContentRadiusFor(double radius) =>
        FlyoutRadiusFor(radius) + (FlyoutBubble / 2) * HoverScale + 14;

    /// <summary>Diameter of the orbit's full painted footprint.</summary>
    public static double WindowSize => ContentRadius * 2;

    /// <summary>
    /// Inner radius of the quarter-circle a hot corner summons. Starts from the orbit's own radius
    /// and grows only as far as it must for four hover-grown items to fit on the quarter arc
    /// without touching each other or the two screen edges (see <see cref="QuadrantLayout"/>).
    /// </summary>
    public static double QuadrantRadius =>
        QuadrantLayout.Radius(QuadrantLayout.ItemCount, Bubble * HoverScale, Radius);

    public const int SlotCount = 8;

    /// <summary>
    /// Slots available to user items: the last two are reserved for the built-in Settings and
    /// Close entries, which are always on the orbit (see <see cref="SystemOrbitItems"/>).
    /// </summary>
    public const int UserSlotCount = SlotCount - 2;

    /// <summary>
    /// The slot (0..<see cref="SlotCount"/>-1) angularly nearest a pointer at offset
    /// (<paramref name="dx"/>, <paramref name="dy"/>) from the orbit center, matching the layout
    /// in OrbitWindow.CreateBubble: slot 0 is straight up, slots advance clockwise 45° apart.
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
