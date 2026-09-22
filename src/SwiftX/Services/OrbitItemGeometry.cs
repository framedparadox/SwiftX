using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SwiftX.Models;
using Windows.Foundation;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace SwiftX.Services;

/// <summary>
/// Turns <see cref="OrbitMetrics.ItemShape"/> into the outline a bubble is drawn with.
/// Circle / squircle / square stay on CornerRadius. Hexagon is a Path (WinUI's Clip is
/// rectangle-only, so a PathGeometry cannot be assigned there).
/// </summary>
public static class OrbitItemGeometry
{
    /// <summary>How far each hexagon vertex is rounded, as a fraction of the bubble size.</summary>
    private const double HexagonCorner = 0.18;

    /// <summary>
    /// Must run after <see cref="Border.Child"/> is assigned — hexagon wraps that child in a
    /// grid with the Path chrome underneath.
    /// </summary>
    public static void Apply(Border border, double size)
    {
        if (OrbitMetrics.ItemShape != OrbitItemShape.Hexagon)
        {
            border.CornerRadius = new CornerRadius(OrbitMetrics.CornerRadiusFor(size));
            return;
        }

        border.CornerRadius = new CornerRadius(0);
        var fill = border.Background;
        var stroke = border.BorderBrush;
        double strokeThickness = border.BorderThickness.Left;
        border.Background = null;
        border.BorderBrush = null;
        border.BorderThickness = new Thickness(0);

        var path = new ShapePath
        {
            Width = size,
            Height = size,
            Data = RoundedHexagon(size),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            Stretch = Stretch.None,
            IsHitTestVisible = false,
        };

        // Detached before it goes into the wrapper grid below — every real caller already has an
        // icon or glyph sitting in border.Child at this point, and WinUI throws
        // ("element is already a child of another element") if that element is handed to a second
        // panel's Children while border still claims it as its own Child. Setting border.Child to
        // null first releases it, which is why this crashed on the very first hexagon bubble drawn
        // rather than only on some edge case.
        var inner = border.Child;
        border.Child = null;

        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(path);
        if (inner is not null)
            grid.Children.Add(inner);
        border.Child = grid;
    }

    /// <summary>
    /// The live acrylic under a bubble. It can only be rounded, never given a hexagon outline
    /// (its edge is a composition rounded-rect clip, and WinUI offers nothing polygonal to swap
    /// in), so under a hexagon it is switched off outright rather than shrunk. A smaller square
    /// left inside the hexagon read as a square plate sitting in front of the hexagon, which is
    /// exactly the defect this replaces. The fill takes over the job of keeping the glyph legible:
    /// see <see cref="RestTint"/>.
    /// </summary>
    public static void Apply(SystemBackdropElement element, double size)
    {
        if (OrbitMetrics.ItemShape == OrbitItemShape.Hexagon)
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        element.CornerRadius = new CornerRadius(OrbitMetrics.CornerRadiusFor(size));
    }

    /// <summary>
    /// The resting fill of a bubble. The theme's glass tint is translucent because it is meant to
    /// sit over a blur; a hexagon has no blur under it (see <see cref="Apply(SystemBackdropElement, double)"/>),
    /// so the same tint would leave the desktop showing through sharply behind the glyph. It is
    /// firmed up to a mostly opaque fill for that shape and left alone for the others.
    /// </summary>
    public static Windows.UI.Color RestTint(Windows.UI.Color glassTint)
    {
        if (OrbitMetrics.ItemShape != OrbitItemShape.Hexagon)
            return glassTint;

        glassTint.A = Math.Max(glassTint.A, HexagonRestAlpha);
        return glassTint;
    }

    /// <summary>Opacity floor of a hexagon's resting fill, out of 255.</summary>
    private const byte HexagonRestAlpha = 0xD0;

    public static Geometry RoundedHexagon(double size)
    {
        double r = size / 2;
        var verts = new Point[6];
        for (int i = 0; i < 6; i++)
        {
            double a = (-90 + i * 60) * Math.PI / 180.0;
            verts[i] = new Point(r + r * Math.Cos(a), r + r * Math.Sin(a));
        }

        double edge = Distance(verts[0], verts[1]);
        double cr = Math.Min(size * HexagonCorner, edge * 0.42);

        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        for (int i = 0; i < 6; i++)
        {
            Point v = verts[i];
            Point prev = verts[(i + 5) % 6];
            Point next = verts[(i + 1) % 6];
            Point p1 = OffsetToward(v, prev, cr);
            Point p2 = OffsetToward(v, next, cr);
            if (i == 0)
                figure.StartPoint = p1;
            else
                figure.Segments.Add(new LineSegment { Point = p1 });
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = v, Point2 = p2 });
        }

        return new PathGeometry { Figures = { figure } };
    }

    private static Point OffsetToward(Point from, Point to, double distance)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) return from;
        double t = distance / len;
        return new Point(from.X + dx * t, from.Y + dy * t);
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
