using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

// ImplicitUsings pulls in System.IO, so the bare name "Path" is already taken by System.IO.Path.
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace SwiftX.Services;

/// <summary>
/// Builds the SwiftX mark that sits in the hub at the dead centre of the radial ring: four
/// gradient petals pinwheeled around a cross-shaped gap. Pure vector, composed in code — a
/// <see cref="Viewbox"/> over a fixed 48x48 canvas of shapes — so it stays crisp at any hub size
/// and any display scale, with no bitmap asset to resample.
/// <para>
/// This is the same mark as the master art in <c>docs/SwiftX.png</c>, which is where the shipped
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
    /// Returns a fresh logo element sized to <paramref name="size"/>. Fresh on every call by
    /// design — a XAML element can only have one parent, so a cached shared instance would
    /// silently vanish from the first ring the moment a second one asked for it.
    /// </summary>
    /// <param name="size">Width and height in DIPs the logo should occupy.</param>
    /// <returns>
    /// The mark as a <see cref="FrameworkElement"/> rather than a bare <see cref="UIElement"/>, so
    /// callers that drop it into a layout panel can align it — a title bar needs to centre it
    /// against the text beside it, and alignment is not on <see cref="UIElement"/>.
    /// </returns>
    public static FrameworkElement Create(double size)
    {
        // A non-positive size would throw when assigned to Width/Height; fall back to the design box
        // so a miscalculated hub radius shows an odd-looking logo instead of taking the ring down.
        double edge = size > 0 ? size : NominalSize;

        var canvas = new Canvas { Width = NominalSize, Height = NominalSize };

        // Addressed by grid position rather than by coordinates: which corners are sharp and which
        // way the gradient runs both fall out of the quadrant a petal sits in, and deriving them
        // beats restating them per petal — the two are easy to get subtly out of step by hand, and
        // a petal with its ramp reversed puts its bright end at the centre instead of the rim.
        canvas.Children.Add(Petal(column: 0, row: 0, PinkOuter, AmberInner));
        canvas.Children.Add(Petal(column: 1, row: 0, TealOuter, BlueInner));
        canvas.Children.Add(Petal(column: 0, row: 1, TealOuter, BlueInner));
        canvas.Children.Add(Petal(column: 1, row: 1, MagentaOuter, SteelInner));

        return new Viewbox
        {
            Width = edge,
            Height = edge,
            Stretch = Stretch.Uniform,
            Child = canvas,
            // The ring hub underneath owns pointer input; the mark must never intercept a click.
            IsHitTestVisible = false,
        };
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
        double left = column == 0 ? 0 : Far;
        double top = row == 0 ? 0 : Far;
        bool sharpOnMainDiagonal = column == row;

        return new ShapePath
        {
            Data = sharpOnMainDiagonal
                ? MainDiagonalOutline(left, top)
                : AntiDiagonalOutline(left, top),
            // The unit-box corner the ramp starts from is the grid position itself; it ends at the
            // corner diagonally opposite, which is the one facing the mark's centre.
            Fill = PetalGradient(new Point(column, row), new Point(1 - column, 1 - row), outer, inner),
        };
    }

    // ---- Geometry ---------------------------------------------------------
    //
    // Built from PathFigure/segment objects rather than parsed path-markup strings: the object model
    // is unambiguously present in WinUI 3 (WPF's Geometry.Parse is not), and unlike a string
    // converter lookup it cannot fail at runtime. Every coordinate is absolute within the design
    // box, so a petal draws itself in the right quadrant with no Canvas offset of its own. Both
    // outlines are traced clockwise, which is what makes every arc a Clockwise sweep.

    /// <summary>
    /// A petal whose sharp corners are top-left and bottom-right. Traced clockwise from the sharp
    /// top-left: along the top edge, round the top-right corner, down to the sharp bottom-right,
    /// back along the bottom edge, round the bottom-left corner, and closed up the left edge.
    /// </summary>
    private static PathGeometry MainDiagonalOutline(double left, double top)
    {
        double right = left + PetalSize;
        double bottom = top + PetalSize;

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
    private static PathGeometry AntiDiagonalOutline(double left, double top)
    {
        double right = left + PetalSize;
        double bottom = top + PetalSize;

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
}
