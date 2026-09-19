using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SwiftX.Controls;

/// <summary>
/// Lays its children out left to right, wraps to a new line when the next child would not fit the
/// width it was given, and centres every line horizontally in that width. WinUI has no wrap panel
/// of its own (the usual one lives in the Community Toolkit, which this app does not reference),
/// and a <c>FlowLayout</c> starts every line at the left edge. A row of small tag chips under a
/// centred card title needs each line centred on the card's own centre line, which is why this
/// exists.
/// <para>
/// The width a line is measured against is the width the panel is given, so the panel wants a
/// bounded width (a card column, not a horizontal scroller) and, to see its lines centred, to be
/// arranged wider than its widest line, which a stretched panel in a column always is. Given an
/// unbounded width it lays everything out on a single line.
/// </para>
/// </summary>
public sealed class CenteredWrapPanel : Panel
{
    private double _itemSpacing;
    private double _lineSpacing;

    /// <summary>Horizontal gap between two children on the same line.</summary>
    public double ItemSpacing
    {
        get => _itemSpacing;
        set
        {
            if (_itemSpacing == value) return;
            _itemSpacing = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Vertical gap between one line and the next.</summary>
    public double LineSpacing
    {
        get => _lineSpacing;
        set
        {
            if (_lineSpacing == value) return;
            _lineSpacing = value;
            InvalidateMeasure();
        }
    }

    private readonly record struct Line(int Start, int Count, double Width, double Height);

    protected override Size MeasureOverride(Size availableSize)
    {
        // Every child is measured against the panel's width, not an unbounded one, so a child
        // wider than the panel is clipped to it rather than pushing the panel past its column.
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));

        var lines = BreakLines(availableSize.Width);
        double width = 0;
        double height = 0;
        foreach (var line in lines)
        {
            width = Math.Max(width, line.Width);
            height += line.Height;
        }
        height += _lineSpacing * Math.Max(0, lines.Count - 1);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var line in BreakLines(finalSize.Width))
        {
            double x = Math.Max(0, (finalSize.Width - line.Width) / 2);
            for (int i = line.Start; i < line.Start + line.Count; i++)
            {
                var child = Children[i];
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, line.Height));
                x += child.DesiredSize.Width + _itemSpacing;
            }
            y += line.Height + _lineSpacing;
        }
        return finalSize;
    }

    /// <summary>
    /// Greedy line breaking over the children's desired sizes: a child joins the current line
    /// unless that would push it past <paramref name="maxWidth"/>, and the first child of a line
    /// always stays on it however wide it is. Run by both measure and arrange, so a final width
    /// that differs from the measured one still breaks (and centres) against the width it is
    /// actually given.
    /// </summary>
    private List<Line> BreakLines(double maxWidth)
    {
        var lines = new List<Line>();
        int start = 0;
        double width = 0;
        double height = 0;

        for (int i = 0; i < Children.Count; i++)
        {
            var size = Children[i].DesiredSize;
            bool firstOnLine = i == start;
            double widthWithChild = firstOnLine ? size.Width : width + _itemSpacing + size.Width;

            if (!firstOnLine && widthWithChild > maxWidth)
            {
                lines.Add(new Line(start, i - start, width, height));
                start = i;
                width = size.Width;
                height = size.Height;
            }
            else
            {
                width = widthWithChild;
                height = Math.Max(height, size.Height);
            }
        }

        if (Children.Count > start)
            lines.Add(new Line(start, Children.Count - start, width, height));
        return lines;
    }
}
