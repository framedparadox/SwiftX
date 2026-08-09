using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SwiftX.Services;

namespace SwiftX.Controls;

/// <summary>Simple title bar for tool windows (solid theme, no Mica).</summary>
public sealed class AppTitleBar : Grid
{
    public AppTitleBar(string glyph, string title)
    {
        Height = 40;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 140, 0),
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
    }
}
