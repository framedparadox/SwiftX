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

    public AppTitleBar(string glyph, string title, FrameworkElement? headerActions = null)
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

        bool isTextGlyph = GlyphFonts.IsTextGlyph(glyph);
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontFamily = isTextGlyph
                ? new FontFamily("Segoe UI")
                : (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            FontSize = isTextGlyph ? 13 : 16,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = glyph.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        content.Children.Add(icon);
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
}
