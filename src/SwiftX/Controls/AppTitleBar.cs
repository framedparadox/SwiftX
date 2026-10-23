using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    private readonly FrameworkElement? _leading;

    private AppTitleBar(FrameworkElement? leading, FrameworkElement? headerActions)
    {
        Height = HeightDip;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _leading = leading;

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
    /// Home already wears the mark as the X in SwiftX, so the caption mark is hidden there and
    /// shown on every other screen — the caption is otherwise empty once you leave Home.
    /// </summary>
    public void SetMarkVisible(bool visible)
    {
        if (_leading is not null)
            _leading.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// A Home button beside the mark, for the one screen whose own header has nowhere left to put
    /// it — the Orbit screen gave up that header slot once both of its sidebars became permanent
    /// (see ShellWindow.xaml's OrbitScreen), so its way back to Home moved up here instead.
    /// Collapsed by default and built lazily on the first call that asks to show it, the same way
    /// every other screen keeps its own Back_Click button rather than this one growing into a
    /// global nav element.
    /// </summary>
    private Button? _homeButton;

    /// <summary>Raised when <see cref="_homeButton"/> is clicked. The caller does whatever its own
    /// Back_Click already does (ending a stale replace, then navigating home) — this control only
    /// draws the button and reports the click.</summary>
    public event RoutedEventHandler? HomeClicked;

    /// <summary>See <see cref="_homeButton"/>'s doc comment. Parallels <see cref="SetMarkVisible"/>:
    /// one call, one bool, called right beside it wherever a screen decides what the caption
    /// shows.</summary>
    public void SetHomeButtonVisible(bool visible)
    {
        if (_homeButton is null)
        {
            if (!visible) return;

            _homeButton = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                // 16 (the mark's own left margin) + AppMarkSize (its width) + 12 (a breathing gap),
                // so this sits immediately to the mark's right rather than on top of it.
                Margin = new Thickness(16 + AppMarkSize + 12, 0, 0, 0),
                Content = new FontIcon
                {
                    Glyph = "",
                    FontSize = 16,
                    FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                },
            };
            AutomationProperties.SetName(_homeButton, "Home");
            ToolTipService.SetToolTip(_homeButton, "Home");
            _homeButton.Click += (_, e) => HomeClicked?.Invoke(this, e);
            Children.Add(_homeButton);
        }

        _homeButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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
