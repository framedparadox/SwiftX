namespace SwiftX.Models;

/// <summary>Which screen corner summons the ring when the pointer is pushed into it.</summary>
public enum HotCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>How much of the ring a hot-corner summon puts on screen.</summary>
public enum HotCornerLayout
{
    /// <summary>The whole ring, exactly as the keyboard shortcut summons it.</summary>
    Ring,

    /// <summary>
    /// Four items only, fanned out along a quarter circle centered on the corner itself — the
    /// pointer is already there, so everything is a short flick away. See
    /// <see cref="QuadrantLayout"/>.
    /// </summary>
    Quadrant,
}

/// <summary>
/// The geometry behind the hot corner: whether a cursor position sits inside the small square
/// patch at one corner of a display. Pure integer math on physical pixels, deliberately free of
/// any windowing type so the rule can be reasoned about (and tested) on its own — the live
/// pointer polling that uses it lives in <c>Services/HotCornerWatcher</c>.
/// </summary>
public static class HotCornerZone
{
    /// <summary>
    /// Side length, in physical pixels, of the trigger patch. Small on purpose: the pointer stops
    /// dead against the edge of the screen when it's thrown at a corner, so a few pixels are all
    /// that is ever needed, and anything larger starts catching deliberate travel past the corner.
    /// </summary>
    public const int Size = 6;

    /// <summary>
    /// True when (<paramref name="x"/>, <paramref name="y"/>) is inside the
    /// <paramref name="size"/>-pixel square at <paramref name="corner"/> of the display whose
    /// bounds start at (<paramref name="left"/>, <paramref name="top"/>). Bounds are the display's
    /// full extent in virtual-screen coordinates, so the left/top of a secondary monitor is
    /// routinely negative. A point outside the display never counts.
    /// </summary>
    public static bool Contains(
        HotCorner corner, int x, int y, int left, int top, int width, int height, int size = Size)
    {
        if (width <= 0 || height <= 0)
            return false;

        int right = left + width;    // exclusive: the far edge pixel is right - 1
        int bottom = top + height;
        if (x < left || x >= right || y < top || y >= bottom)
            return false;

        // A patch can never span more than half a display, so a nonsensical size can't turn one
        // corner into the whole screen (or two opposite corners into overlapping zones).
        int patchWidth = Math.Clamp(size, 1, Math.Max(1, width / 2));
        int patchHeight = Math.Clamp(size, 1, Math.Max(1, height / 2));

        bool nearLeft = x < left + patchWidth;
        bool nearRight = x >= right - patchWidth;
        bool nearTop = y < top + patchHeight;
        bool nearBottom = y >= bottom - patchHeight;

        return corner switch
        {
            HotCorner.TopLeft => nearLeft && nearTop,
            HotCorner.TopRight => nearRight && nearTop,
            HotCorner.BottomLeft => nearLeft && nearBottom,
            HotCorner.BottomRight => nearRight && nearBottom,
            _ => false,
        };
    }
}
