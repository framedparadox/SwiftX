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

    /// <summary>
    /// The colour of the ring drawn over the acrylic disc's hard edge: the glass tint's own colour
    /// at (nearly) full strength. It has to be close to opaque to do its job — whatever of the
    /// acrylic's stair-step shows through it is the contrast between glass and desktop times what
    /// is left of the ring's transparency — and the glass tint's own colour is what makes that read
    /// as the glass's bezel rather than as a second material.
    /// </summary>
    public static Windows.UI.Color RimTint(Windows.UI.Color glassTint)
    {
        glassTint.A = Math.Max(glassTint.A, RimAlpha);
        return glassTint;
    }

    /// <summary>Opacity floor of the rim, out of 255.</summary>
    private const byte RimAlpha = 0xF0;

    // The acrylic under a bubble is clipped by the compositor to a rounded rectangle whose edge is a
    // hard stair-step rather than an antialiased curve. The original treatment held the disc a little
    // way inside the bubble's edge and covered that inset band with an opaque ring — but on a dark
    // glass there is no ring colour that both hides the edge and is not itself a dark circle around
    // every bubble, and dropping the ring instead left the inset band showing through as a *lighter*
    // ring (tint over the raw desktop, where the interior is tint over the blurred acrylic). Either
    // way the edge read as a rim. So the inset is now zero: the acrylic fills the whole bubble, the
    // fill is the same glass from centre to edge, and the tint's own antialiased circle is the
    // silhouette. The ring is kept only under a light glass, where it still reads as a pale bezel
    // rather than a dark outline (see OrbitWindow.CreateBubble).

    /// <summary>
    /// How far, in device pixels, the acrylic disc is held inside the bubble's edge, per side. Zero:
    /// the acrylic fills the bubble so the glass is consistent edge to edge, with no tint-only band
    /// at the rim. See the block comment above for why the earlier inset-plus-ring treatment was
    /// dropped.
    /// </summary>
    public const double BackdropInsetPx = 0;

    /// <summary>
    /// Width, in device pixels, of the pale bezel ring drawn over the acrylic's edge under a light
    /// glass only (dark glasses drop it — see the block comment above and OrbitWindow.CreateBubble).
    /// </summary>
    public const double RimWidthPx = 3;

    /// <summary>How far, in device pixels, the acrylic clip's edge can stand outside its nominal
    /// size. Measured on the live dock at 100% scaling: up to one pixel.</summary>
    public const double AcrylicEdgeOvershootPx = 1;

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

    /// <summary>
    /// Snaps a group-arc notch angle to the nearest face normal or corner of the current
    /// item shape, so the notch sits exactly on a geometric feature rather than at an
    /// arbitrary inter-feature angle. Circle is returned unchanged — its boundary is uniform.
    /// <list type="bullet">
    ///   <item>Hexagon (pointy-top): 6 face normals every 60° starting at 0°.</item>
    ///   <item>Square / Squircle: 4 face normals (0°, 90°, 180°, 270°) plus 4 corners
    ///     (45°, 135°, 225°, 315°), snap step 45°.</item>
    /// </list>
    /// </summary>
    public static double SnapNotchAngle(double angleRad)
    {
        double step = OrbitMetrics.ItemShape switch
        {
            // 6 face normals + 6 vertex angles interleaved every 30°, so items snap to
            // whichever geometric feature (flat face OR vertex chevron) is nearest.
            OrbitItemShape.Hexagon  => Math.PI / 6,
            OrbitItemShape.Square   => Math.PI / 4,  // 4 faces + 4 corners, every 45°
            OrbitItemShape.Squircle => Math.PI / 4,
            _                       => 0,            // Circle: no snapping
        };
        return step == 0 ? angleRad : Math.Round(angleRad / step) * step;
    }

    /// <summary>
    /// Distance from the bubble center to its edge at <paramref name="angleRad"/>, for the
    /// current <see cref="OrbitMetrics.ItemShape"/>. Used by the group-arc draw code so the
    /// notch hugs the shape's actual boundary rather than always following a circle.
    /// </summary>
    public static double EdgeDistanceAt(double diameter, double angleRad)
    {
        double r = diameter / 2;
        return OrbitMetrics.ItemShape switch
        {
            OrbitItemShape.Square => SquareEdgeAt(r, angleRad),
            OrbitItemShape.Squircle => SquircleEdgeAt(r, OrbitMetrics.CornerRadiusFor(diameter), angleRad),
            OrbitItemShape.Hexagon => HexagonEdgeAt(r, angleRad),
            _ => r,
        };
    }

    /// <summary>Distance from center to the edge of an axis-aligned square with half-side
    /// <paramref name="halfSide"/> at angle <paramref name="angleRad"/>.</summary>
    private static double SquareEdgeAt(double halfSide, double angleRad)
    {
        double cosA = Math.Abs(Math.Cos(angleRad));
        double sinA = Math.Abs(Math.Sin(angleRad));
        double d = Math.Max(cosA, sinA);
        return d < 0.001 ? halfSide : halfSide / d;
    }

    /// <summary>Distance from center to the edge of a rounded square (squircle) with half-side
    /// <paramref name="halfSide"/> and corner radius <paramref name="cornerRadius"/>.</summary>
    private static double SquircleEdgeAt(double halfSide, double cornerRadius, double angleRad)
    {
        // Fold to the first octant [0, 45°] from the nearest axis.
        double a = Math.Abs(angleRad % Math.PI);
        if (a > Math.PI / 2) a = Math.PI - a;
        if (a > Math.PI / 4) a = Math.PI / 2 - a;

        double cosA = Math.Cos(a), sinA = Math.Sin(a);
        double s = halfSide, cr = cornerRadius;
        double A = s - cr; // distance from center to corner-arc center along each axis

        // Ray hits the flat side when it reaches x=s before the corner arc.
        if (sinA * s <= A * cosA)
            return s / Math.Max(cosA, 0.001);

        // Ray hits the corner arc: solve (t·cosA − A)² + (t·sinA − A)² = cr²
        double bHalf = A * (cosA + sinA);
        double c = 2 * A * A - cr * cr;
        return bHalf + Math.Sqrt(Math.Max(0, bHalf * bHalf - c));
    }

    /// <summary>Distance from center to the edge of a pointy-top regular hexagon with
    /// circumradius <paramref name="circumRadius"/> at angle <paramref name="angleRad"/>.</summary>
    private static double HexagonEdgeAt(double circumRadius, double angleRad)
    {
        // Edge normals are every 60° starting from −60°. Find the angle relative to the
        // nearest edge normal, then use the apothem / cos(relative) formula.
        double inradius = circumRadius * Math.Sqrt(3) / 2;
        double step = Math.PI / 3;
        double offset = angleRad + Math.PI / 3;
        double normalized = offset - step * Math.Round(offset / step);
        return inradius / Math.Cos(Math.Clamp(normalized, -Math.PI / 6, Math.PI / 6));
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
