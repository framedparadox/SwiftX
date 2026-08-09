using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

// ImplicitUsings pulls in System.IO, so the bare name "Path" is already taken by System.IO.Path.
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace SwiftX.Services;

/// <summary>
/// Builds the SwiftX mark that sits in the hub at the dead centre of the radial ring. Pure vector,
/// composed in code — a <see cref="Viewbox"/> over a fixed 48x48 canvas of shapes — so it stays
/// crisp at any hub size and any display scale, with no bitmap asset to resample.
/// <para>
/// This is a <b>placeholder</b>: it draws the coloured Windows Security shield so hub placement and
/// sizing can be eyeballed before real branding exists. When the real SwiftX mark lands, replace
/// the geometry and palette below — <see cref="Create"/>'s signature is the whole contract callers
/// depend on, so nothing outside this file needs to change.
/// </para>
/// </summary>
public static class AppLogo
{
    /// <summary>
    /// Design box every coordinate below is expressed in. The Viewbox scales this to the caller's
    /// requested size, which is what keeps the mark sharp rather than pixel-doubling it.
    /// </summary>
    private const double NominalSize = 48d;

    // Hard-coded rather than ThemeResource on purpose: the app has hard black and hard white
    // themes, and this mark has to read as *coloured* on both, not invert with the theme.
    private static readonly Windows.UI.Color ShieldDark = Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x62, 0xC8);
    private static readonly Windows.UI.Color ShieldLight = Windows.UI.Color.FromArgb(0xFF, 0x2A, 0xA9, 0xF0);
    private static readonly Windows.UI.Color ShieldAccent = Windows.UI.Color.FromArgb(0xFF, 0x7F, 0xD9, 0xFF);
    private static readonly Windows.UI.Color CheckWhite = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>
    /// Returns a fresh logo element sized to <paramref name="size"/>. Fresh on every call by
    /// design — a XAML element can only have one parent, so a cached shared instance would
    /// silently vanish from the first ring the moment a second one asked for it.
    /// <para>
    /// Placeholder art (a coloured Windows Security shield); swap the geometry in this file for the
    /// real SwiftX branding when it exists.
    /// </para>
    /// </summary>
    /// <param name="size">Width and height in DIPs the logo should occupy.</param>
    public static UIElement Create(double size)
    {
        // A non-positive size would throw when assigned to Width/Height; fall back to the design box
        // so a miscalculated hub radius shows an odd-looking logo instead of taking the ring down.
        double edge = size > 0 ? size : NominalSize;

        var canvas = new Canvas { Width = NominalSize, Height = NominalSize };
        canvas.Children.Add(ShieldBody());
        canvas.Children.Add(ShieldFacet());
        canvas.Children.Add(CheckMark());

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
    /// The shield itself: blue gradient fill, plus a light cyan rim so the dark top-left end of the
    /// gradient still separates from a pure-black backdrop (the rim stays subtle against white,
    /// where the blue body is already doing the work).
    /// </summary>
    private static ShapePath ShieldBody() => new()
    {
        Data = ShieldOutline(),
        Fill = ShieldGradient(),
        Stroke = new SolidColorBrush(ShieldAccent),
        StrokeThickness = 1.5,
        StrokeLineJoin = PenLineJoin.Round,
    };

    /// <summary>
    /// The right half of the shield, tinted with the cyan accent to give the mark its faceted look.
    /// Held well under full opacity so the white check mark on top keeps its contrast.
    /// </summary>
    private static ShapePath ShieldFacet() => new()
    {
        Data = ShieldRightHalf(),
        Fill = new SolidColorBrush(ShieldAccent),
        Opacity = 0.38,
    };

    /// <summary>The white check, drawn as a stroked open figure with round caps and join.</summary>
    private static ShapePath CheckMark()
    {
        var figure = NewFigure(16.5, 23.5, closed: false, filled: false);
        figure.Segments.Add(Line(21.5, 28.5));
        figure.Segments.Add(Line(31.5, 17.5));

        return new ShapePath
        {
            Data = ToGeometry(figure),
            Stroke = new SolidColorBrush(CheckWhite),
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
    }

    // ---- Geometry ---------------------------------------------------------
    //
    // Built from PathFigure/segment objects rather than parsed path-markup strings: the object model
    // is unambiguously present in WinUI 3 (WPF's Geometry.Parse is not), and unlike a string
    // converter lookup it cannot fail at runtime.

    /// <summary>
    /// Shield outline within the 48x48 box: flat top with rounded corners, straight flanks down to
    /// roughly mid-height, then swept inwards to a point at the bottom. Traced clockwise starting
    /// just past the top-left corner. Everything stays inside 7.25..40.75 x 4.25..44.75 once the
    /// 1.5 stroke is accounted for, so nothing touches the edge of the design box.
    /// </summary>
    private static PathGeometry ShieldOutline()
    {
        var figure = NewFigure(11, 5, closed: true, filled: true);
        figure.Segments.Add(Line(37, 5));                       // top edge
        figure.Segments.Add(Quad(40, 5, 40, 8));                // top-right corner
        figure.Segments.Add(Line(40, 27));                      // right flank
        figure.Segments.Add(Cubic(40, 35, 34, 41, 24, 44));     // sweep in to the bottom point
        figure.Segments.Add(Cubic(14, 41, 8, 35, 8, 27));       // and back out, mirrored
        figure.Segments.Add(Line(8, 8));                        // left flank
        figure.Segments.Add(Quad(8, 5, 11, 5));                 // top-left corner
        return ToGeometry(figure);
    }

    /// <summary>
    /// The same outline clipped to the x &gt;= 24 half. Closing the figure draws the straight centre
    /// line back up, which becomes the facet's inner edge.
    /// </summary>
    private static PathGeometry ShieldRightHalf()
    {
        var figure = NewFigure(24, 5, closed: true, filled: true);
        figure.Segments.Add(Line(37, 5));
        figure.Segments.Add(Quad(40, 5, 40, 8));
        figure.Segments.Add(Line(40, 27));
        figure.Segments.Add(Cubic(40, 35, 34, 41, 24, 44));
        return ToGeometry(figure);
    }

    /// <summary>Top-left to bottom-right blue ramp, relative to the shield's own bounds.</summary>
    private static LinearGradientBrush ShieldGradient()
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection(),
        };
        brush.GradientStops.Add(new GradientStop { Color = ShieldDark, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = ShieldLight, Offset = 1 });
        return brush;
    }

    // The child collections are assigned explicitly rather than assumed to be pre-created, so the
    // Add calls above are safe no matter how the WinUI defaults behave.

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

    private static QuadraticBezierSegment Quad(double cx, double cy, double x, double y) =>
        new() { Point1 = new Point(cx, cy), Point2 = new Point(x, y) };

    private static BezierSegment Cubic(
        double c1x, double c1y, double c2x, double c2y, double x, double y) =>
        new() { Point1 = new Point(c1x, c1y), Point2 = new Point(c2x, c2y), Point3 = new Point(x, y) };
}
