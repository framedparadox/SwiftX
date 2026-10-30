using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SwiftX.Models;
using Windows.Foundation;

// ImplicitUsings pulls in System.IO, so the bare name "Path" is already taken by System.IO.Path.
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace SwiftX.Services;

/// <summary>
/// Builds the SwiftX mark that sits in the hub at the dead centre of the radial orbit: four
/// gradient petals pinwheeled around a cross-shaped gap. Pure vector, composed in code — a
/// <see cref="Viewbox"/> over a fixed 48x48 canvas of shapes — so it stays crisp at any hub size
/// and any display scale, with no bitmap asset to resample.
/// <para>
/// Two forms of it: <see cref="Create"/> for the flat mark a title bar or an About card wants, and
/// <see cref="CreateFloating"/> for the dock's hub, which adds the drop shadow that lifts it off
/// the desktop. Either can be played in and out — see <see cref="Mark"/> for the motion.
/// </para>
/// <para>
/// This is the same mark as the master art in <c>img/SwiftX.png</c>, which is where the shipped
/// <c>.ico</c> and the MSIX tiles come from (see <c>tools/generate-brand-assets.py</c>). The
/// geometry and palette below were measured off that master, so a change to the brand means
/// re-exporting the master, re-running that script, and updating the constants here to match.
/// </para>
/// </summary>
public static class AppLogo
{
    /// <summary>
    /// Design box every coordinate below is expressed in. The Viewbox scales this to the caller's
    /// requested size, which is what keeps the mark sharp rather than pixel-doubling it.
    /// </summary>
    private const double NominalSize = 48d;

    /// <summary>
    /// Half the width of the cross-shaped channel that separates the four petals. The master puts
    /// the gap at roughly 2.4% of the mark's width; at 48 units that lands just over one unit
    /// across, which still reads as a clean split once the mark is scaled down to hub size.
    /// </summary>
    private const double HalfGap = 0.6d;

    /// <summary>Edge of one petal's square. Two of them plus the channel fill the design box.</summary>
    private const double PetalSize = (NominalSize - 2 * HalfGap) / 2;

    /// <summary>
    /// Radius of a petal's two rounded corners, measured off the master at ~46.6% of the petal
    /// edge. Large enough that each petal reads as a leaf rather than a chamfered square.
    /// </summary>
    private const double CornerRadius = PetalSize * 0.466d;

    /// <summary>Where the second column and second row of petals begin.</summary>
    private const double Far = PetalSize + 2 * HalfGap;

    // Hard-coded rather than ThemeResource on purpose: the app has hard black and hard white
    // themes, and this mark has to read as *coloured* on both, not invert with the theme. Each
    // pair runs from the petal's outer corner to the corner that faces the centre of the mark.
    private static readonly Windows.UI.Color PinkOuter = Rgb(0xFF, 0x0A, 0x76);
    private static readonly Windows.UI.Color AmberInner = Rgb(0xFF, 0xD5, 0x10);
    private static readonly Windows.UI.Color TealOuter = Rgb(0x2C, 0xD0, 0x98);
    private static readonly Windows.UI.Color BlueInner = Rgb(0x1D, 0x4A, 0xDB);
    private static readonly Windows.UI.Color MagentaOuter = Rgb(0xF1, 0x06, 0x7E);
    private static readonly Windows.UI.Color SteelInner = Rgb(0x1A, 0x65, 0xB5);

    /// <summary>
    /// Returns a fresh, flat logo element sized to <paramref name="size"/> — no shadow, no
    /// entrance motion. This is the mark as a title bar or an About card wants it: a static
    /// piece of branding sitting on a solid surface.
    /// <para>
    /// Fresh on every call by design — a XAML element can only have one parent, so a cached
    /// shared instance would silently vanish from the first orbit the moment a second one asked
    /// for it.
    /// </para>
    /// </summary>
    /// <param name="size">Width and height in DIPs the logo should occupy.</param>
    /// <returns>
    /// The mark as a <see cref="Mark"/> — a <see cref="FrameworkElement"/> rather than a bare
    /// <see cref="UIElement"/>, so callers that drop it into a layout panel can align it: a title
    /// bar needs to centre it against the text beside it, and alignment is not on
    /// <see cref="UIElement"/>.
    /// </returns>
    public static Mark Create(double size) => new(size, floating: false);

    /// <summary>
    /// The same mark, but lifted off whatever is behind it by a soft drop shadow — the form the
    /// dock's hub wants, where the logo is painted straight onto the desktop with no plate under
    /// it and would otherwise read as pasted flat against the wallpaper.
    /// </summary>
    /// <param name="size">Width and height in DIPs the logo should occupy.</param>
    public static Mark CreateFloating(double size) => new(size, floating: true);

    /// <summary>
    /// The Configure Orbit icon for a bubble: the bundled orbit-ring PNG
    /// (<see cref="SystemOrbitItems.ConfigureOrbitImagePath"/>) when present, falling back to the
    /// flat SwiftX AppLogo mark — the same fallback the live orbit uses via
    /// <c>OrbitWindow.CreateConfigureOrbitBubbleIcon</c>. Size is the icon area in DIPs, not the
    /// bubble diameter: match what the call site passes for the glyph or inner logo area.
    /// </summary>
    public static FrameworkElement CreateConfigureIcon(double size)
    {
        var path = SystemOrbitItems.ConfigureOrbitImagePath.Value;
        if (!string.IsNullOrEmpty(path))
        {
            return new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Source = new BitmapImage(new Uri(path)),
            };
        }
        return Create(size);
    }

    /// <summary>
    /// Springs a single glyph the way <see cref="Mark.PlayEntrance"/> springs the pinwheel, but as
    /// one piece and clockwise — the Home Tools mark, so it rhymes with Customize without replaying
    /// the same four-petal choreography.
    /// </summary>
    public static void PlayGlyphEntrance(FrameworkElement element)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("RotationAngleInDegrees");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");

            float w = (float)(element.ActualWidth > 0 ? element.ActualWidth
                : element.Width > 0 ? element.Width : 96);
            float h = (float)(element.ActualHeight > 0 ? element.ActualHeight
                : element.Height > 0 ? element.Height : 96);
            visual.CenterPoint = new Vector3(w / 2f, h / 2f, 0);
            // Opposite the mark's -90°: Tools turns the other way so the two Home motions
            // don't look like copies of each other.
            visual.RotationAngleInDegrees = 90f;
            visual.Scale = new Vector3(0.45f, 0.45f, 1);
            visual.Opacity = 0;

            var compositor = visual.Compositor;
            var spin = compositor.CreateSpringScalarAnimation();
            spin.FinalValue = 0f;
            spin.DampingRatio = 0.62f;
            spin.Period = SpringMotion.Period(220);
            visual.StartAnimation("RotationAngleInDegrees", spin);

            var grow = compositor.CreateSpringVector3Animation();
            grow.FinalValue = Vector3.One;
            grow.DampingRatio = 0.62f;
            grow.Period = SpringMotion.Period(220);
            visual.StartAnimation("Scale", grow);

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1, 1);
            fade.Duration = TimeSpan.FromMilliseconds(220);
            visual.StartAnimation("Opacity", fade);
        }
        catch
        {
            // Composition can throw if the element has no compositor yet; the next timer tick retries.
        }
    }

    /// <summary>
    /// The four petals as a hot-corner picker: top-left / top-right / bottom-right / bottom-left
    /// leaves map onto those screen corners. The selected leaf is full strength; the other three
    /// sit dimmed so the mark still reads as the logo. When <paramref name="onPicked"/> is set,
    /// each leaf is a hit target; otherwise this is a static glyph (the orbit preview's corner
    /// button).
    /// </summary>
    /// <param name="onlyActive">
    /// Skips the other three leaves entirely instead of dimming them, leaving just the selected
    /// corner's own petal. Used where a dimmed four-leaf mark would read as the full logo (and so
    /// be confused with the Configure Orbit bubble, which always shows all four at full strength) —
    /// the orbit canvas's own corner button, never the sidebar's four-way picker.
    /// </param>
    public static FrameworkElement CreateCornerGlyph(
        double size, HotCorner selected, Action<HotCorner>? onPicked = null, bool onlyActive = false)
    {
        double edge = size > 0 ? size : NominalSize;
        var canvas = new Canvas
        {
            Width = NominalSize,
            Height = NominalSize,
            IsHitTestVisible = false,
        };

        void AddPetal(int column, int row, Windows.UI.Color outer, Windows.UI.Color inner, HotCorner corner)
        {
            bool isSelected = corner == selected;
            if (onlyActive && !isSelected)
                return;

            var petal = Petal(column, row, outer, inner);
            petal.Opacity = isSelected ? 1 : DimmedPetalOpacity;
            petal.IsHitTestVisible = false;
            // Read back by SetCornerGlyphHighlighted, which has to know which leaf to leave lit
            // when the pointer goes away.
            petal.Tag = isSelected ? SelectedPetalTag : null;
            // The static form is the orbit canvas's corner button, whose leaves fade up to the
            // whole logo on hover (see SetCornerGlyphHighlighted). Assigned before the petal is in
            // the tree, so the opacity above is not itself animated in.
            if (onPicked is null)
                petal.OpacityTransition = new ScalarTransition { Duration = PetalHighlightDuration };
            canvas.Children.Add(petal);
        }

        AddPetal(0, 0, PinkOuter, AmberInner, HotCorner.TopLeft);
        AddPetal(1, 0, TealOuter, BlueInner, HotCorner.TopRight);
        AddPetal(1, 1, MagentaOuter, SteelInner, HotCorner.BottomRight);
        AddPetal(0, 1, TealOuter, BlueInner, HotCorner.BottomLeft);

        var art = new Viewbox
        {
            Width = edge,
            Height = edge,
            Stretch = Stretch.Uniform,
            Child = canvas,
            IsHitTestVisible = false,
        };

        if (onPicked is null)
            return art;

        var root = new Grid { Width = edge, Height = edge };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumnSpan(art, 2);
        Grid.SetRowSpan(art, 2);
        root.Children.Add(art);

        void AddHit(int column, int row, HotCorner corner, string name)
        {
            var hit = new Button
            {
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            hit.Resources["ButtonBackground"] = transparent;
            hit.Resources["ButtonBackgroundPointerOver"] = transparent;
            hit.Resources["ButtonBackgroundPressed"] = transparent;
            hit.Resources["ButtonBorderBrush"] = transparent;
            hit.Resources["ButtonBorderBrushPointerOver"] = transparent;
            hit.Resources["ButtonBorderBrushPressed"] = transparent;
            ToolTipService.SetToolTip(hit, name);
            AutomationProperties.SetName(hit, name);
            var picked = corner;
            hit.Click += (_, _) => onPicked(picked);
            Grid.SetColumn(hit, column);
            Grid.SetRow(hit, row);
            root.Children.Add(hit);
        }

        AddHit(0, 0, HotCorner.TopLeft, "Top left");
        AddHit(1, 0, HotCorner.TopRight, "Top right");
        AddHit(0, 1, HotCorner.BottomLeft, "Bottom left");
        AddHit(1, 1, HotCorner.BottomRight, "Bottom right");
        return root;
    }

    /// <summary>How strongly the three unselected leaves of a corner glyph are drawn at rest.</summary>
    private const double DimmedPetalOpacity = 0.32;

    /// <summary>The fade between a corner glyph's resting and highlighted states.</summary>
    private static readonly TimeSpan PetalHighlightDuration = TimeSpan.FromMilliseconds(140);

    /// <summary>Marks the selected leaf, so un-highlighting knows which one stays lit.</summary>
    private static readonly object SelectedPetalTag = new();

    /// <summary>
    /// Lights all four leaves of a static <see cref="CreateCornerGlyph"/> — the full-colour logo —
    /// or returns it to rest, where only the selected corner's leaf is at full strength.
    /// <para>
    /// This is the orbit canvas corner button's hover. It deliberately keeps each leaf's own
    /// gradient rather than repainting the mark in the accent colour the canvas's other glyph
    /// buttons tint to: a flat accent fill turned the one piece of branding on the canvas into a
    /// single-colour silhouette, and the dimmed leaves stayed dimmed, so the hover read as a
    /// recolour of the corner picker rather than as the logo lighting up.
    /// </para>
    /// </summary>
    /// <param name="glyph">A glyph from <see cref="CreateCornerGlyph"/> with no picker attached.
    /// Anything else is left untouched.</param>
    public static void SetCornerGlyphHighlighted(FrameworkElement glyph, bool highlighted)
    {
        if (glyph is not Viewbox { Child: Canvas canvas })
            return;
        foreach (var child in canvas.Children)
        {
            if (child is ShapePath petal)
                petal.Opacity = highlighted || ReferenceEquals(petal.Tag, SelectedPetalTag)
                    ? 1
                    : DimmedPetalOpacity;
        }
    }

    // ---- Shapes -----------------------------------------------------------

    /// <summary>
    /// One petal: a square with two opposite corners left sharp and the other two rounded off hard.
    /// The sharp pair always straddles the mark's centre — one corner points into the middle, its
    /// opposite marks the outer corner of the whole mark — which is what gives the four of them
    /// their pinwheel.
    /// <para>
    /// Both of those facts follow from the quadrant. A petal's outer corner is the corner of its own
    /// square that matches its grid position: the top-left petal's is its (0,0) corner, the
    /// bottom-right petal's its (1,1). The sharp corners are the pair on the diagonal through it, so
    /// the diagonal is the main one exactly when column and row agree.
    /// </para>
    /// </summary>
    /// <param name="column">0 for the left pair of petals, 1 for the right.</param>
    /// <param name="row">0 for the top pair, 1 for the bottom.</param>
    /// <param name="outer">Colour at the corner pointing away from the mark's centre.</param>
    /// <param name="inner">Colour at the corner pointing into it.</param>
    private static ShapePath Petal(
        int column, int row, Windows.UI.Color outer, Windows.UI.Color inner)
    {
        bool sharpOnMainDiagonal = column == row;

        var petal = new ShapePath
        {
            Data = sharpOnMainDiagonal ? MainDiagonalOutline() : AntiDiagonalOutline(),
            // The unit-box corner the ramp starts from is the grid position itself; it ends at the
            // corner diagonally opposite, which is the one facing the mark's centre.
            Fill = PetalGradient(new Point(column, row), new Point(1 - column, 1 - row), outer, inner),
        };

        // Placed by the Canvas rather than by drawing the outline out at the quadrant's own
        // coordinates. Both put the petal in the same spot, but only this way is the Path's own
        // element box one petal exactly — which is what lets a petal rotate about its own centre
        // (Mark's entrance) and hand out an alpha mask that is just itself (Mark's shadow).
        Canvas.SetLeft(petal, Offset(column));
        Canvas.SetTop(petal, Offset(row));
        return petal;
    }

    /// <summary>Where the petal in column (or row) <paramref name="index"/> starts.</summary>
    private static double Offset(int index) => index == 0 ? 0 : Far;

    // ---- Geometry ---------------------------------------------------------
    //
    // Built from PathFigure/segment objects rather than parsed path-markup strings: the object model
    // is unambiguously present in WinUI 3 (WPF's Geometry.Parse is not), and unlike a string
    // converter lookup it cannot fail at runtime. Both outlines are drawn in a petal-sized box of
    // their own at the origin, and the Canvas puts each one in its quadrant (see Petal). Both are
    // traced clockwise, which is what makes every arc a Clockwise sweep.

    /// <summary>
    /// A petal whose sharp corners are top-left and bottom-right. Traced clockwise from the sharp
    /// top-left: along the top edge, round the top-right corner, down to the sharp bottom-right,
    /// back along the bottom edge, round the bottom-left corner, and closed up the left edge.
    /// </summary>
    private static PathGeometry MainDiagonalOutline()
    {
        const double left = 0, top = 0, right = PetalSize, bottom = PetalSize;

        var figure = NewFigure(left, top, closed: true, filled: true);
        figure.Segments.Add(Line(right - CornerRadius, top));
        figure.Segments.Add(Arc(right, top + CornerRadius));
        figure.Segments.Add(Line(right, bottom));
        figure.Segments.Add(Line(left + CornerRadius, bottom));
        figure.Segments.Add(Arc(left, bottom - CornerRadius));
        return ToGeometry(figure);
    }

    /// <summary>
    /// The same petal turned a quarter turn: sharp corners at top-right and bottom-left. Traced
    /// clockwise from the sharp top-right, down the right edge first.
    /// </summary>
    private static PathGeometry AntiDiagonalOutline()
    {
        const double left = 0, top = 0, right = PetalSize, bottom = PetalSize;

        var figure = NewFigure(right, top, closed: true, filled: true);
        figure.Segments.Add(Line(right, bottom - CornerRadius));
        figure.Segments.Add(Arc(right - CornerRadius, bottom));
        figure.Segments.Add(Line(left, bottom));
        figure.Segments.Add(Line(left, top + CornerRadius));
        figure.Segments.Add(Arc(left + CornerRadius, top));
        return ToGeometry(figure);
    }

    /// <summary>
    /// The petal's two-stop ramp, running corner to corner across its own bounding box. Relative
    /// mapping means the caller can name the endpoints as unit-box corners and let each petal's own
    /// bounds do the rest.
    /// </summary>
    /// <param name="from">Unit-box corner the ramp starts at: the petal's outer corner.</param>
    /// <param name="to">The corner diagonally opposite, facing the mark's centre.</param>
    private static LinearGradientBrush PetalGradient(
        Point from, Point to, Windows.UI.Color outer, Windows.UI.Color inner)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = from,
            EndPoint = to,
            GradientStops = new GradientStopCollection(),
        };
        brush.GradientStops.Add(new GradientStop { Color = outer, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = inner, Offset = 1 });
        return brush;
    }

    // The child collections are assigned explicitly rather than assumed to be pre-created, so the
    // Add calls above are safe no matter how the WinUI defaults behave.

    private static Windows.UI.Color Rgb(byte r, byte g, byte b) =>
        Windows.UI.Color.FromArgb(0xFF, r, g, b);

    private static PathFigure NewFigure(double x, double y, bool closed, bool filled) => new()
    {
        StartPoint = new Point(x, y),
        IsClosed = closed,
        IsFilled = filled,
        Segments = new PathSegmentCollection(),
    };

    private static PathGeometry ToGeometry(PathFigure figure)
    {
        var geometry = new PathGeometry { Figures = new PathFigureCollection() };
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static LineSegment Line(double x, double y) =>
        new() { Point = new Point(x, y) };

    /// <summary>
    /// A petal's rounded corner: a quarter of a circle of <see cref="CornerRadius"/>, always the
    /// short way round and always clockwise, since both outlines above are traced clockwise.
    /// </summary>
    private static ArcSegment Arc(double x, double y) => new()
    {
        Point = new Point(x, y),
        Size = new Size(CornerRadius, CornerRadius),
        RotationAngle = 0,
        IsLargeArc = false,
        SweepDirection = SweepDirection.Clockwise,
    };

    // ---- The mark ---------------------------------------------------------

    /// <summary>
    /// The mark itself: the four petals in a <see cref="Viewbox"/>, optionally over a soft drop
    /// shadow, and able to play itself in and out.
    /// <para>
    /// The motion is the pinwheel the mark already draws, put in motion. Coming in, the whole
    /// mark swings a quarter turn into place while each petal — starting at the top-left and
    /// going round clockwise, a beat apart — untwists and grows out of nothing into its own
    /// quadrant, so the four of them assemble rather than appearing at once. Going out is that
    /// same motion rewound: the last petal to arrive is the first to leave, each twists back the
    /// way it came, and the mark unwinds its quarter turn as it fades. The dock's hub drives both
    /// (<c>OrbitWindow.BuildHub</c> / <c>OrbitWindow.FadeOutHub</c>) so the mark's motion and the
    /// orbit's own entrance are one gesture.
    /// </para>
    /// <para>
    /// The hub replays that same entrance whenever the pointer arrives over it, so the entrance is
    /// a motion the user sees many times rather than once — which is why <see cref="PlayEntrance"/>
    /// goes to the trouble of guaranteeing that every run of it is identical.
    /// </para>
    /// <para>
    /// Only the petal layer turns — the shadow underneath stays put, the way a shadow does when
    /// the thing above it spins rather than the light moving.
    /// </para>
    /// </summary>
    public sealed class Mark : Grid
    {
        // Entrance motion. The same spring family and period the orbit's own bubbles come in on
        // (OrbitWindow.SpringDamping / SpringPeriodMs), so the hub is visibly part of one motion
        // rather than running its own timeline; the stagger is per petal rather than per bubble.
        private const float SpringDamping = 0.62f;
        private const int SpringPeriodMs = 220;
        private const int PetalStaggerMs = 34;

        // Exit motion. Shorter than the entrance and on an ease rather than a spring: the orbit is
        // on its way out, and a spring's overshoot at that point reads as a bounce nobody asked
        // for. The whole thing has to finish inside the dismiss OrbitWindow waits out before it
        // hides the window, so exit + three staggers stays under the inner orbit's own budget.
        private const int ExitMs = 180;
        private const int PetalExitStaggerMs = 24;

        /// <summary>Quarter turn the whole mark swings through on the way in, and back out on the
        /// way out. Anticlockwise, against the clockwise order the petals arrive in.</summary>
        private const float SpinDegrees = -90f;

        /// <summary>How far a petal is twisted out of true before it settles into its quadrant.</summary>
        private const float TwistDegrees = -55f;

        /// <summary>How small a petal starts — small enough to read as growing, large enough that
        /// the gradient is still legible while it does.</summary>
        private const float StartScale = 0.45f;

        // Shadow, all as fractions of the mark's own size so it holds at any hub size. The offset
        // is small and downward: the mark should read as floating a little above the desktop, not
        // as a card thrown across it.
        private const double ShadowBlurFraction = 0.26;
        private const double ShadowDropFraction = 0.07;
        private const float ShadowOpacity = 0.5f;

        /// <summary>
        /// The four petals in the order they animate: clockwise from the top-left, which is the
        /// order the pinwheel turns in. Quadrant and palette sit in one row so the two cannot drift
        /// apart, and the array's own order is the stagger — index is beat.
        /// </summary>
        private static readonly (int Column, int Row, Windows.UI.Color Outer, Windows.UI.Color Inner)[] Quadrants =
        {
            (0, 0, PinkOuter, AmberInner),
            (1, 0, TealOuter, BlueInner),
            (1, 1, MagentaOuter, SteelInner),
            (0, 1, TealOuter, BlueInner),
        };

        /// <summary>
        /// Roughly how long one entrance stays in flight, measured from the moment
        /// <see cref="PlayEntrance"/> is called: the last petal's beat, plus a couple of spring
        /// periods for it to travel out and settle. A spring has no fixed duration — it ends when
        /// it stops moving — so this is deliberately an estimate of when the motion stops
        /// <em>reading</em> as motion rather than an exact end time, and the one thing that depends
        /// on it treats it as exactly that: <c>OrbitWindow.ReplayHubEntranceOnHover</c> uses it
        /// only to tell a fresh hover from a re-trigger landing on top of a replay already playing,
        /// where being a frame out either way costs nothing.
        /// <para>
        /// It sits down here rather than up with the constants it is built from because it also
        /// reads <see cref="Quadrants"/>, and static field initializers run in textual order: put
        /// above the array it would see an empty one and quietly lose the stagger.
        /// </para>
        /// </summary>
        public static readonly int EntranceSpanMs =
            PetalStaggerMs * (Quadrants.Length - 1) + SpringPeriodMs * 2;

        /// <summary>
        /// How long <see cref="PlayExit"/> runs: the last petal's delay plus one petal's exit. A
        /// keyframe motion, so unlike <see cref="EntranceSpanMs"/> this one is exact — and it is what
        /// a caller that tears the mark down afterwards has to wait out.
        /// </summary>
        public static readonly int ExitSpanMs =
            PetalExitStaggerMs * (Quadrants.Length - 1) + ExitMs;

        // Home Orbit idle: small fixed hub + eight dock bubbles on a ring that slowly revolves.
        // Overlay lives in DIP space beside the Viewbox so bubble offsets are not Viewbox-scaled
        // when the hub shrinks.
        private const float DockHubScale = 0.18f;
        private const int DockRevolveMs = 16000;
        private const double DockRadiusDesign = 19d;
        private const double DockBubbleDesign = 4.4d;

        // Home Tools idle: 4×4 grid is the rest mark; cells tile-spin then settle.
        private const int EncodeMs = 3000;
        private const int EncodeCellStaggerMs = 35;
        private const double EncodeCellDesign = 11.2d;
        private const double EncodeCellStepDesign = 12d;
        private const double EncodeCellInsetDesign = 0.4d;
        private const double EncodeCellRadiusDesign = 1.8d;

        private readonly double _edge;
        private readonly float _unit;
        private readonly Viewbox _art;
        private readonly ShapePath[] _petals;
        private readonly Border? _shadowHost;
        private bool _shadowAttached;

        private Canvas? _overlay;
        private Canvas? _dockRing;
        private Ellipse? _orbitGuide;
        private Ellipse[]? _dockBubbles;
        private Border[]? _encodeCells;
        private bool _dockIdleActive;

        internal Mark(double size, bool floating)
        {
            // A non-positive size would throw when assigned to Width/Height; fall back to the design
            // box so a miscalculated hub radius shows an odd-looking logo instead of taking the orbit
            // down.
            _edge = size > 0 ? size : NominalSize;
            _unit = (float)(_edge / NominalSize);
            Width = _edge;
            Height = _edge;
            // The orbit hub underneath owns pointer input; the mark must never intercept a click.
            IsHitTestVisible = false;

            if (floating)
            {
                // First child, so it sits under the petals. Empty of XAML content — everything it
                // shows is the composition shadow AttachShadow hangs off it.
                _shadowHost = new Border { Width = _edge, Height = _edge, IsHitTestVisible = false };
                Children.Add(_shadowHost);
            }

            _petals = new ShapePath[Quadrants.Length];
            var canvas = new Canvas { Width = NominalSize, Height = NominalSize };
            for (int i = 0; i < Quadrants.Length; i++)
            {
                var (column, row, outer, inner) = Quadrants[i];
                _petals[i] = Petal(column, row, outer, inner);
                canvas.Children.Add(_petals[i]);
            }

            _art = new Viewbox
            {
                Width = _edge,
                Height = _edge,
                Stretch = Stretch.Uniform,
                Child = canvas,
                IsHitTestVisible = false,
            };
            Children.Add(_art);

            // The shadow is masked by what the petals actually render, so it can only be built once
            // they have. Loaded fires again every time the hub is re-attached to the orbit canvas,
            // hence the one-shot guard inside.
            if (floating)
                Loaded += (_, _) => AttachShadow();
        }

        /// <summary>
        /// Plays the mark in. Safe to call before the element is loaded — composition animations
        /// run off the element's visual, which exists from the moment it does.
        /// <para>
        /// Every run starts from the same frame no matter what state the mark was left in, because
        /// the first thing it does is <see cref="Reset"/> — which stops every animation this method
        /// is about to start, before a single start value is seeded. That order is the whole point,
        /// not tidiness. Composition hands a running animation ownership of the property it drives,
        /// and an assignment to an owned property is silently dropped: seeding
        /// <c>RotationAngleInDegrees</c>, a petal's <c>Scale</c> or its <c>Opacity</c> while an
        /// earlier run is still playing did nothing at all, and the spring started an instant later
        /// then picked up from whatever intermediate value that earlier run happened to be passing
        /// through. A replay therefore began on an arbitrary frame and looked different every time
        /// — worst of all under exactly the input that provokes it, a pointer crossing the hub
        /// repeatedly. Stopping first hands each property back, so the seeds land and the
        /// choreography below is reproducible frame for frame.
        /// </para>
        /// <para>
        /// The rest state <see cref="Reset"/> snaps to on its way past is never seen: it and the
        /// seeding below happen in the same turn of the UI thread, and the compositor only commits
        /// once at the end of it, so the seeded values are the only ones ever presented. Reusing it
        /// rather than repeating its list of stops here is also what keeps the two in step — a
        /// property added to the choreography below has to be added to <see cref="Reset"/> to snap
        /// back at all, so it cannot be left un-stopped here by omission.
        /// </para>
        /// </summary>
        public void PlayEntrance()
        {
            try
            {
                // Stop, then seed. The reverse order silently does nothing whenever a previous run
                // is still in flight — see this method's own remarks.
                Reset();

                var art = ArtVisual();
                art.RotationAngleInDegrees = SpinDegrees;

                var compositor = art.Compositor;
                var spin = compositor.CreateSpringScalarAnimation();
                spin.FinalValue = 0f;
                spin.DampingRatio = SpringDamping;
                spin.Period = SpringMotion.Period(SpringPeriodMs);
                art.StartAnimation("RotationAngleInDegrees", spin);

                for (int i = 0; i < _petals.Length; i++)
                {
                    var petal = PetalVisual(i);
                    petal.RotationAngleInDegrees = TwistDegrees;
                    petal.Scale = new Vector3(StartScale, StartScale, 1);
                    petal.Opacity = 0;

                    // SetInitialValueBeforeDelay is the default for both animation families, so a
                    // petal holds the state set above until its own beat comes round rather than
                    // sitting there fully drawn waiting for its turn.
                    var delay = TimeSpan.FromMilliseconds(i * PetalStaggerMs);

                    var untwist = compositor.CreateSpringScalarAnimation();
                    untwist.FinalValue = 0f;
                    untwist.DampingRatio = SpringDamping;
                    untwist.Period = SpringMotion.Period(SpringPeriodMs);
                    untwist.DelayTime = delay;
                    petal.StartAnimation("RotationAngleInDegrees", untwist);

                    var grow = compositor.CreateSpringVector3Animation();
                    grow.FinalValue = Vector3.One;
                    grow.DampingRatio = SpringDamping;
                    grow.Period = SpringMotion.Period(SpringPeriodMs);
                    grow.DelayTime = delay;
                    petal.StartAnimation("Scale", grow);

                    var fadeIn = compositor.CreateScalarKeyFrameAnimation();
                    fadeIn.InsertKeyFrame(1, 1);
                    fadeIn.Duration = TimeSpan.FromMilliseconds(SpringPeriodMs);
                    fadeIn.DelayTime = delay;
                    petal.StartAnimation("Opacity", fadeIn);
                }
            }
            catch
            {
                // Motion is decoration: a mark that cannot animate still has to draw.
            }
        }

        /// <summary>
        /// A full clockwise turn of the whole mark, petals breathing in reverse order — the Home
        /// Tools replay, so it is the same pinwheel as Customize without the same entrance.
        /// </summary>
        public void PlayPulse()
        {
            try
            {
                Reset();

                var art = ArtVisual();
                var compositor = art.Compositor;

                var spin = compositor.CreateScalarKeyFrameAnimation();
                spin.InsertKeyFrame(0, 0);
                spin.InsertKeyFrame(1, 360f);
                spin.Duration = TimeSpan.FromMilliseconds(720);
                art.StartAnimation("RotationAngleInDegrees", spin);

                int last = _petals.Length - 1;
                for (int i = 0; i < _petals.Length; i++)
                {
                    var petal = PetalVisual(i);
                    var delay = TimeSpan.FromMilliseconds((last - i) * PetalStaggerMs);

                    var breathe = compositor.CreateVector3KeyFrameAnimation();
                    breathe.InsertKeyFrame(0, Vector3.One);
                    breathe.InsertKeyFrame(0.4f, new Vector3(0.58f, 0.58f, 1));
                    breathe.InsertKeyFrame(1, Vector3.One);
                    breathe.Duration = TimeSpan.FromMilliseconds(520);
                    breathe.DelayTime = delay;
                    petal.StartAnimation("Scale", breathe);
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Home Orbit idle: hub stays small and fixed at the ring centre; eight dock bubbles
        /// slowly revolve — matching the “Slow drift” mockup (no join-back).
        /// </summary>
        public void PlayOrbitDock()
        {
            try
            {
                // Continuous drift: do not restart a ring that is already revolving.
                if (_dockIdleActive) return;

                Reset();
                EnsureOverlay();
                ShowDockOverlay();

                var art = ArtVisual();
                var hubScale = new Vector3(DockHubScale, DockHubScale, 1);
                art.Scale = hubScale;

                if (_shadowHost is not null)
                {
                    var shadow = ElementCompositionPreview.GetElementVisual(_shadowHost);
                    shadow.CenterPoint = new Vector3((float)(_edge / 2), (float)(_edge / 2), 0);
                    shadow.Scale = hubScale;
                }

                float hub = (float)_edge / 2f;
                float bubbleR = (float)(DockBubbleDesign * _unit);
                float radius = (float)(DockRadiusDesign * _unit);

                for (int i = 0; i < OrbitMetrics.SlotCount; i++)
                {
                    var ellipse = _dockBubbles![i];
                    double angle = OrbitMetrics.SlotAngleRad(i);
                    float cx = hub + radius * (float)Math.Cos(angle);
                    float cy = hub + radius * (float)Math.Sin(angle);
                    Canvas.SetLeft(ellipse, cx - bubbleR);
                    Canvas.SetTop(ellipse, cy - bubbleR);
                    ellipse.Opacity = 1;

                    // Scale and opacity only, never Offset. A dock bubble's Offset is how XAML
                    // carries the Canvas.Left/Top set just above, and XAML only writes it again when
                    // that position changes. Every replay sets the same position as the first play,
                    // so an Offset written here stood, and all eight bubbles revolved as one clump
                    // in the ring's top-left corner. That happened on every replay after a Reset,
                    // which the Home rest now does routinely (see ShellWindow.StopHomeOrbitMark).
                    var visual = ElementCompositionPreview.GetElementVisual(ellipse);
                    visual.Scale = Vector3.One;
                    visual.Opacity = 1;
                }

                if (_orbitGuide is not null)
                {
                    _orbitGuide.Opacity = 0.85;
                    var guide = ElementCompositionPreview.GetElementVisual(_orbitGuide);
                    guide.Opacity = 0.85f;
                }

                var ring = ElementCompositionPreview.GetElementVisual(_dockRing!);
                ring.CenterPoint = new Vector3(hub, hub, 0);
                ring.RotationAngleInDegrees = 0;

                var spin = ring.Compositor.CreateScalarKeyFrameAnimation();
                spin.InsertKeyFrame(0f, 0f);
                spin.InsertKeyFrame(1f, 360f);
                spin.Duration = TimeSpan.FromMilliseconds(DockRevolveMs);
                spin.IterationBehavior = AnimationIterationBehavior.Forever;
                ring.StartAnimation("RotationAngleInDegrees", spin);

                _dockIdleActive = true;
            }
            catch
            {
                // Motion is decoration: a mark that cannot animate still has to draw.
            }
        }

        /// <summary>
        /// Home Tools idle: the 4×4 grid is the rest mark; each cell quarter-turns then settles
        /// — matching the “Tile spin” mockup.
        /// </summary>
        public void PlayEncode()
        {
            try
            {
                Reset();
                EnsureOverlay();
                ShowEncodeOverlay();

                // Grid is the mark — hide the petals for the whole idle.
                for (int i = 0; i < _petals.Length; i++)
                {
                    var petal = PetalVisual(i);
                    petal.Opacity = 0;
                }

                var art = ArtVisual();
                var compositor = art.Compositor;
                var ease = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.4f, 0f), new Vector2(0.2f, 1f));

                float cell = (float)(EncodeCellDesign * _unit);
                float half = cell / 2f;
                float step = (float)(EncodeCellStepDesign * _unit);
                float inset = (float)(EncodeCellInsetDesign * _unit);
                var duration = TimeSpan.FromMilliseconds(EncodeMs);

                for (int k = 0; k < 16; k++)
                {
                    int col = k % 4;
                    int row = k / 4;
                    var border = _encodeCells![k];
                    var visual = ElementCompositionPreview.GetElementVisual(border);
                    visual.CenterPoint = new Vector3(half, half, 0);

                    float x = inset + col * step;
                    float y = inset + row * step;
                    visual.Offset = new Vector3(x, y, 0);
                    visual.Opacity = 1;
                    visual.Scale = Vector3.One;
                    visual.RotationAngleInDegrees = 0;
                    border.Opacity = 1;

                    var delay = TimeSpan.FromMilliseconds(k * EncodeCellStaggerMs);

                    // cell-spin: hold → 90° + scale 0.9 → settle
                    var spin = compositor.CreateScalarKeyFrameAnimation();
                    spin.InsertKeyFrame(0f, 0f);
                    spin.InsertKeyFrame(0.10f, 0f);
                    spin.InsertKeyFrame(0.35f, 90f, ease);
                    spin.InsertKeyFrame(0.55f, 0f, ease);
                    spin.InsertKeyFrame(1f, 0f);
                    spin.Duration = duration;
                    spin.DelayTime = delay;

                    var scale = compositor.CreateVector3KeyFrameAnimation();
                    scale.InsertKeyFrame(0f, Vector3.One);
                    scale.InsertKeyFrame(0.10f, Vector3.One);
                    scale.InsertKeyFrame(0.35f, new Vector3(0.9f, 0.9f, 1), ease);
                    scale.InsertKeyFrame(0.55f, Vector3.One, ease);
                    scale.InsertKeyFrame(1f, Vector3.One);
                    scale.Duration = duration;
                    scale.DelayTime = delay;

                    visual.StartAnimation("RotationAngleInDegrees", spin);
                    visual.StartAnimation("Scale", scale);
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Plays the mark back out — <see cref="PlayEntrance"/> rewound, last petal in first out.
        /// <para>
        /// Deliberately does <em>not</em> stop what is playing first, unlike <see cref="PlayEntrance"/>.
        /// Starting an animation on a property replaces whatever was driving it, and every keyframe
        /// below names only its end value, so each one picks up from wherever the entrance had got
        /// to and carries on out from there. That is exactly right for an exit: an orbit dismissed
        /// while it is still arriving should flow back out of the frame it reached, not jump to a
        /// canonical starting pose first. The entrance is the opposite case — it has a start frame
        /// it must actually begin on — which is why only it seeds values and therefore only it has
        /// to clear the way for them.
        /// </para>
        /// </summary>
        public void PlayExit()
        {
            try
            {
                var art = ArtVisual();
                var compositor = art.Compositor;
                var ease = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.3f, 0f), new Vector2(0.2f, 1f));

                var unwind = compositor.CreateScalarKeyFrameAnimation();
                unwind.InsertKeyFrame(1, SpinDegrees, ease);
                unwind.Duration = TimeSpan.FromMilliseconds(ExitMs);
                art.StartAnimation("RotationAngleInDegrees", unwind);

                int last = _petals.Length - 1;
                for (int i = 0; i < _petals.Length; i++)
                {
                    var petal = PetalVisual(i);
                    var delay = TimeSpan.FromMilliseconds((last - i) * PetalExitStaggerMs);

                    var twist = compositor.CreateScalarKeyFrameAnimation();
                    twist.InsertKeyFrame(1, TwistDegrees, ease);
                    twist.Duration = TimeSpan.FromMilliseconds(ExitMs);
                    twist.DelayTime = delay;
                    petal.StartAnimation("RotationAngleInDegrees", twist);

                    var shrink = compositor.CreateVector3KeyFrameAnimation();
                    shrink.InsertKeyFrame(1, new Vector3(StartScale, StartScale, 1), ease);
                    shrink.Duration = TimeSpan.FromMilliseconds(ExitMs);
                    shrink.DelayTime = delay;
                    petal.StartAnimation("Scale", shrink);

                    var fadeOut = compositor.CreateScalarKeyFrameAnimation();
                    fadeOut.InsertKeyFrame(1, 0, ease);
                    fadeOut.Duration = TimeSpan.FromMilliseconds(ExitMs);
                    fadeOut.DelayTime = delay;
                    petal.StartAnimation("Opacity", fadeOut);
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Drops every animation and snaps the mark to rest. For the hub being reused: a dismiss
        /// that a new summon interrupted leaves <see cref="PlayExit"/>'s half-faded petals applied
        /// to these very visuals, and without this the orbit would come up with a mark part way out
        /// the door.
        /// <para>
        /// It is also the first thing <see cref="PlayEntrance"/> does, for the stopping rather than
        /// the snapping — see there for why an entrance cannot seed its start frame until the
        /// animations that own those properties have let go of them. Between the two callers, the
        /// list of stops below has to cover every property the entrance animates: miss one and the
        /// mark neither resets cleanly nor replays identically.
        /// </para>
        /// </summary>
        public void Reset()
        {
            try
            {
                var art = ArtVisual();
                art.StopAnimation("RotationAngleInDegrees");
                art.StopAnimation("Scale");
                art.RotationAngleInDegrees = 0;
                art.Scale = Vector3.One;

                if (_shadowHost is not null)
                {
                    var shadow = ElementCompositionPreview.GetElementVisual(_shadowHost);
                    shadow.StopAnimation("Scale");
                    shadow.Scale = Vector3.One;
                }

                for (int i = 0; i < _petals.Length; i++)
                {
                    var petal = PetalVisual(i);
                    petal.StopAnimation("RotationAngleInDegrees");
                    petal.StopAnimation("Scale");
                    petal.StopAnimation("Opacity");
                    petal.RotationAngleInDegrees = 0;
                    petal.Scale = Vector3.One;
                    petal.Opacity = 1;
                }

                HideOverlays();
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// DIP-space overlay beside the Viewbox so dock bubbles and encode cells keep absolute
        /// offsets while the petal layer scales. Built once on first Home idle play.
        /// </summary>
        private void EnsureOverlay()
        {
            if (_overlay is not null) return;

            _overlay = new Canvas
            {
                Width = _edge,
                Height = _edge,
                IsHitTestVisible = false,
            };

            float bubble = (float)(DockBubbleDesign * _unit * 2);
            float hub = (float)_edge / 2f;
            float orbitR = (float)(DockRadiusDesign * _unit);

            _dockRing = new Canvas
            {
                Width = _edge,
                Height = _edge,
                IsHitTestVisible = false,
            };

            _orbitGuide = new Ellipse
            {
                Width = orbitR * 2,
                Height = orbitR * 2,
                Stroke = new SolidColorBrush(Rgb(0x80, 0x80, 0x90)),
                StrokeThickness = Math.Max(1, _unit * 0.35),
                StrokeDashArray = new DoubleCollection { 1.2, 1.6 },
                Opacity = 0,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(_orbitGuide, hub - orbitR);
            Canvas.SetTop(_orbitGuide, hub - orbitR);
            _dockRing.Children.Add(_orbitGuide);

            _dockBubbles = new Ellipse[OrbitMetrics.SlotCount];
            for (int i = 0; i < OrbitMetrics.SlotCount; i++)
            {
                int q = i % Quadrants.Length;
                var (column, row, outer, inner) = Quadrants[q];
                var ellipse = new Ellipse
                {
                    Width = bubble,
                    Height = bubble,
                    Fill = PetalGradient(new Point(column, row), new Point(1 - column, 1 - row), outer, inner),
                    Opacity = 0,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(ellipse, 0);
                Canvas.SetTop(ellipse, 0);
                _dockBubbles[i] = ellipse;
                _dockRing.Children.Add(ellipse);
            }

            _overlay.Children.Add(_dockRing);

            float cell = (float)(EncodeCellDesign * _unit);
            float radius = (float)(EncodeCellRadiusDesign * _unit);
            _encodeCells = new Border[16];
            for (int k = 0; k < 16; k++)
            {
                int col = k % 4;
                int row = k / 4;
                // Quadrant fill: TL/TR on top row-pair, BL/BR on bottom — same as the HTML mockup.
                int q = row < 2 ? (col < 2 ? 0 : 1) : (col < 2 ? 3 : 2);
                var (c, r, outer, inner) = Quadrants[q];
                var border = new Border
                {
                    Width = cell,
                    Height = cell,
                    CornerRadius = new CornerRadius(radius),
                    Background = PetalGradient(new Point(c, r), new Point(1 - c, 1 - r), outer, inner),
                    Opacity = 0,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(border, 0);
                Canvas.SetTop(border, 0);
                _encodeCells[k] = border;
                _overlay.Children.Add(border);
            }

            Children.Add(_overlay);
        }

        private void ShowDockOverlay()
        {
            if (_overlay is null || _dockRing is null || _dockBubbles is null) return;
            _overlay.Visibility = Visibility.Visible;
            _dockRing.Visibility = Visibility.Visible;
            if (_orbitGuide is not null)
                _orbitGuide.Visibility = Visibility.Visible;
            foreach (var bubble in _dockBubbles)
                bubble.Visibility = Visibility.Visible;
            if (_encodeCells is not null)
            {
                foreach (var cell in _encodeCells)
                    cell.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowEncodeOverlay()
        {
            if (_overlay is null || _encodeCells is null) return;
            _overlay.Visibility = Visibility.Visible;
            if (_dockRing is not null)
                _dockRing.Visibility = Visibility.Collapsed;
            if (_orbitGuide is not null)
                _orbitGuide.Visibility = Visibility.Collapsed;
            if (_dockBubbles is not null)
            {
                foreach (var bubble in _dockBubbles)
                    bubble.Visibility = Visibility.Collapsed;
            }
            foreach (var cell in _encodeCells)
                cell.Visibility = Visibility.Visible;
        }

        private void HideOverlays()
        {
            if (_overlay is null)
            {
                _dockIdleActive = false;
                return;
            }

            try
            {
                if (_dockRing is not null)
                {
                    var ring = ElementCompositionPreview.GetElementVisual(_dockRing);
                    ring.StopAnimation("RotationAngleInDegrees");
                    ring.RotationAngleInDegrees = 0;
                    _dockRing.Visibility = Visibility.Collapsed;
                }

                if (_orbitGuide is not null)
                {
                    var guide = ElementCompositionPreview.GetElementVisual(_orbitGuide);
                    guide.StopAnimation("Opacity");
                    guide.Opacity = 0;
                    _orbitGuide.Opacity = 0;
                    _orbitGuide.Visibility = Visibility.Collapsed;
                }

                if (_dockBubbles is not null)
                {
                    foreach (var bubble in _dockBubbles)
                    {
                        // No Offset here: layout owns it for these Canvas-positioned bubbles (see
                        // PlayOrbitDock). The encode cells below are the opposite case: they sit at
                        // Canvas 0,0 and are placed by Offset alone, so resetting theirs is correct.
                        var visual = ElementCompositionPreview.GetElementVisual(bubble);
                        visual.StopAnimation("Scale");
                        visual.StopAnimation("Opacity");
                        visual.Opacity = 0;
                        visual.Scale = Vector3.One;
                        bubble.Opacity = 0;
                        bubble.Visibility = Visibility.Collapsed;
                    }
                }

                if (_encodeCells is not null)
                {
                    foreach (var cell in _encodeCells)
                    {
                        var visual = ElementCompositionPreview.GetElementVisual(cell);
                        visual.StopAnimation("Offset");
                        visual.StopAnimation("Scale");
                        visual.StopAnimation("Opacity");
                        visual.StopAnimation("RotationAngleInDegrees");
                        visual.Opacity = 0;
                        visual.Scale = Vector3.One;
                        visual.Offset = Vector3.Zero;
                        visual.RotationAngleInDegrees = 0;
                        cell.Opacity = 0;
                        cell.Visibility = Visibility.Collapsed;
                    }
                }

                _overlay.Visibility = Visibility.Collapsed;
                _dockIdleActive = false;
            }
            catch
            {
                _dockIdleActive = false;
            }
        }

        /// <summary>
        /// The petal layer's visual, set to turn about the middle of the mark rather than its
        /// top-left corner.
        /// </summary>
        private Visual ArtVisual()
        {
            var visual = ElementCompositionPreview.GetElementVisual(_art);
            visual.CenterPoint = new Vector3((float)(_edge / 2), (float)(_edge / 2), 0);
            return visual;
        }

        /// <summary>
        /// One petal's visual, set to turn and grow about the middle of its own square. That is the
        /// middle of the Path's own element box — the outline is drawn at the origin of a
        /// petal-sized box and the Canvas moves it into its quadrant (see <see cref="Petal"/>) — so
        /// the centre is the same for all four regardless of which quadrant they sit in.
        /// </summary>
        private Visual PetalVisual(int index)
        {
            var visual = ElementCompositionPreview.GetElementVisual(_petals[index]);
            visual.CenterPoint = new Vector3((float)(PetalSize / 2), (float)(PetalSize / 2), 0);
            return visual;
        }

        /// <summary>
        /// Hangs the drop shadow off the host behind the petals: one shadow per petal, each masked
        /// by what that petal actually paints, so the shadow is the mark's own pinwheel silhouette
        /// rather than a rectangle behind it.
        /// <para>
        /// The host is a plain <see cref="Border"/> with nothing in it, so the whole shadow is
        /// composition-side and casts outside the host's own bounds without needing anything
        /// unclipped in the XAML tree above it. If a mask cannot be had, the sprite falls back to
        /// its own bounds — a plainer shadow, but the mark still floats.
        /// </para>
        /// </summary>
        private void AttachShadow()
        {
            if (_shadowHost is null || _shadowAttached) return;

            try
            {
                var host = ElementCompositionPreview.GetElementVisual(_shadowHost);
                var compositor = host.Compositor;

                // The petals are laid out in the 48-unit design box and the Viewbox scales that to
                // the mark's real size; the shadow is composition-side and gets no such scaling for
                // free, so every measurement below is converted by hand.
                float scale = (float)(_edge / NominalSize);
                float blur = (float)(_edge * ShadowBlurFraction);
                var drop = new Vector3(0, (float)(_edge * ShadowDropFraction), 0);
                var black = Rgb(0, 0, 0);

                var group = compositor.CreateContainerVisual();
                group.Size = new Vector2((float)_edge, (float)_edge);

                for (int i = 0; i < _petals.Length; i++)
                {
                    var (column, row, _, _) = Quadrants[i];

                    var shadow = compositor.CreateDropShadow();
                    shadow.Color = black;
                    shadow.Opacity = ShadowOpacity;
                    shadow.BlurRadius = blur;
                    shadow.Offset = drop;
                    shadow.Mask = _petals[i].GetAlphaMask();

                    var caster = compositor.CreateSpriteVisual();
                    caster.Size = new Vector2((float)PetalSize * scale, (float)PetalSize * scale);
                    caster.Offset = new Vector3(
                        (float)Offset(column) * scale, (float)Offset(row) * scale, 0);
                    caster.Shadow = shadow;
                    group.Children.InsertAtTop(caster);
                }

                ElementCompositionPreview.SetElementChildVisual(_shadowHost, group);
                _shadowAttached = true;
            }
            catch
            {
                // A shadow is decoration: if composition will not give one, the mark draws flat.
            }
        }
    }
}
