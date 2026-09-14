using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SwiftX.Services;

namespace SwiftX.Controls;

/// <summary>Simple title bar for tool windows (solid theme, no Mica).</summary>
public sealed class AppTitleBar : Grid
{
    /// <summary>
    /// Room reserved on the right for the OS-drawn minimize/maximize/close buttons. A page's
    /// <see cref="HeaderActions"/> (see <c>ToolPages.ToolPage</c>) is anchored just to the left
    /// of this zone rather than given a Grid column of its own — the caption has nothing else in
    /// it for those buttons to collide with, but the reservation still has to hold: the OS chrome
    /// paints there regardless of what this control puts down.
    /// </summary>
    private const double SystemButtonZoneWidth = 140;

    /// <summary>
    /// Edge of the SwiftX mark when a title bar leads with it. Slightly over the 16px glyph size
    /// below because the mark is four separate petals around a gap rather than one solid glyph, so
    /// it needs a touch more room to carry the same visual weight.
    /// </summary>
    private const double AppMarkSize = 18;

    public AppTitleBar(string glyph, FrameworkElement? headerActions = null)
        : this(GlyphIcon(glyph), headerActions)
    {
    }

    /// <summary>
    /// A title bar led by the SwiftX mark rather than a glyph, for the app's own windows (Settings,
    /// Add to orbit) as opposed to the individual tools. Those windows extend their content into the
    /// caption, so Windows never draws the window icon for them — without this they carry no mark
    /// at all.
    /// </summary>
    public static AppTitleBar WithAppMark(FrameworkElement? headerActions = null) =>
        new(AppLogo.Create(AppMarkSize), headerActions);

    /// <summary>
    /// The caption strip's fixed height in DIPs. Public because a window that sizes itself to its
    /// own content has to add this on top of what that content measures — the title bar is a
    /// sibling row, not part of it. See <c>AddItemWindow.FitWindowToContent</c>.
    /// </summary>
    public const double HeightDip = 40;

    /// <summary>
    /// No title text is drawn here — the caption carries the mark or the tool's glyph alone. What
    /// a window <em>is</em> still has two real answers, neither of which lived in this strip: the
    /// real <c>Window.Title</c> every caller already sets (what Alt+Tab, the taskbar and its
    /// thumbnail tooltip read), and — for the windows that open on more than one thing (Add to
    /// orbit vs. Edit, a specific tool) — an in-body heading (<c>AddItemWindow</c>'s
    /// <c>HeadingText</c>, <c>OnboardingWindow</c>'s <c>StepTitle</c>) that already said the same
    /// thing again, right under this bar. A caption that repeated it a third time, in a strip whose
    /// job is otherwise just to host the mark and drag the window, was the one copy worth cutting.
    /// </summary>
    private AppTitleBar(FrameworkElement? leading, FrameworkElement? headerActions)
    {
        Height = HeightDip;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        if (leading is not null)
        {
            leading.HorizontalAlignment = HorizontalAlignment.Left;
            leading.VerticalAlignment = VerticalAlignment.Center;
            leading.Margin = new Thickness(16, 0, 0, 0);
            Children.Add(leading);
        }

        if (headerActions is not null)
        {
            headerActions.HorizontalAlignment = HorizontalAlignment.Right;
            headerActions.VerticalAlignment = VerticalAlignment.Center;
            headerActions.Margin = new Thickness(0, 0, SystemButtonZoneWidth, 0);
            Children.Add(headerActions);
        }
    }

    /// <summary>
    /// The leading icon for a glyph-led title bar, or <see langword="null"/> for the empty glyph
    /// that means "no icon". Text glyphs come from Segoe UI at a smaller size; everything else is
    /// a symbol from the theme's icon font.
    /// </summary>
    private static FontIcon? GlyphIcon(string glyph)
    {
        if (glyph.Length == 0)
            return null;

        bool isTextGlyph = GlyphFonts.IsTextGlyph(glyph);
        return new FontIcon
        {
            Glyph = glyph,
            FontFamily = isTextGlyph
                ? new FontFamily("Segoe UI")
                : (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            FontSize = isTextGlyph ? 13 : 16,
        };
    }
}
