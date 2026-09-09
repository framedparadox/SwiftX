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
    /// of this zone rather than given a Grid column of its own — titles here are always short
    /// ("Data", "Certificate"), so the two can never collide.
    /// </summary>
    private const double SystemButtonZoneWidth = 140;

    /// <summary>
    /// Edge of the SwiftX mark when a title bar leads with it. Slightly over the 16px glyph size
    /// below because the mark is four separate petals around a gap rather than one solid glyph, so
    /// it needs a touch more room to carry the same visual weight beside the title.
    /// </summary>
    private const double AppMarkSize = 18;

    public AppTitleBar(string glyph, string title, FrameworkElement? headerActions = null)
        : this(GlyphIcon(glyph), title, headerActions)
    {
    }

    /// <summary>
    /// A title bar led by the SwiftX mark rather than a glyph, for the app's own windows (Settings,
    /// Add to ring) as opposed to the individual tools. Those windows extend their content into the
    /// caption, so Windows never draws the window icon for them — without this they carry no mark
    /// at all.
    /// </summary>
    public static AppTitleBar WithAppMark(string title, FrameworkElement? headerActions = null) =>
        new(AppLogo.Create(AppMarkSize), title, headerActions);

    private AppTitleBar(FrameworkElement? leading, string title, FrameworkElement? headerActions)
    {
        Height = 40;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(16, 0, 0, 0),
        };

        // Left out entirely when there is nothing to lead with, rather than added collapsed, so the
        // title sits at the panel's margin either way. Centred here rather than by each caller: a
        // fixed-size element in a horizontal StackPanel is not centred by default, so the mark would
        // otherwise ride high against the title.
        if (leading is not null)
        {
            leading.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(leading);
        }

        content.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Children.Add(content);

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
