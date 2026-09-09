using SwiftX.Services;
using SwiftX.Services.Syntax;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SwiftX.Controls;

/// <summary>
/// Read-only, colourised output view (design doc §10.2). v1 renders the whole document into one
/// selectable <see cref="TextBlock"/> (<c>IsTextSelectionEnabled</c> gives real click-drag
/// selection across colour boundaries "for free", covering §10.2's "no free-form cross-line
/// drag-select" concern without needing the plain-text-view fallback) rather than the fully
/// virtualized per-line <c>ItemsRepeater</c> the design document describes — see §29 risk #1,
/// whose own prescribed fallback ("cap live highlighting at a smaller threshold and lean harder
/// on the plain-text path") is exactly what <see cref="MaxHighlightLength"/> below does.
/// </summary>
public sealed class CodeView : Grid
{
    /// <summary>Above this length, colouring is skipped and the raw text is shown plain — the
    /// §22 "large input" fallback, in lieu of full incremental virtualization.</summary>
    public const int MaxHighlightLength = 300_000;

    private readonly TextBlock _gutter = new()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 13,
        TextAlignment = TextAlignment.Right,
        Opacity = 0.45,
        Margin = new Thickness(8, 8, 8, 8),
    };

    /// <summary>
    /// Deliberately <b>not</b> wrapped. A wrapped line takes several rows on screen while the
    /// gutter beside it still spends one row per line, so from the first long line onwards every
    /// number pointed at the wrong row — which is worse than no gutter at all when the status bar
    /// has just told you the error is on line 40. Long lines scroll horizontally instead.
    /// </summary>
    private readonly TextBlock _content = new()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 13,
        TextWrapping = TextWrapping.NoWrap,
        IsTextSelectionEnabled = true,
        Margin = new Thickness(0, 8, 8, 8),
    };

    private string _text = "";
    private IReadOnlyList<Token> _tokens = [];
    private bool _showLineNumbers = true;

    /// <summary>
    /// Token brushes, memoized per kind and theme.
    /// <para>
    /// <see cref="Render"/> assigns a foreground to one <see cref="Run"/> per token, and used to
    /// construct a <see cref="SolidColorBrush"/> for each — thousands of identical brushes per
    /// repaint over a document approaching <see cref="MaxHighlightLength"/>. There are only ever
    /// as many distinct brushes as there are <see cref="TokenKind"/> values, so they are built
    /// once and shared. The theme is part of the key because <see cref="SyntaxPalette"/> answers
    /// differently in Light and Dark, and both live in the cache across a theme switch: the
    /// entries are tiny and a switch is not worth invalidating for.
    /// </para>
    /// <para>
    /// Deliberately per instance, not static. A <see cref="SolidColorBrush"/> is a
    /// <see cref="DependencyObject"/> with thread affinity to the XAML tree that first uses it,
    /// and SwiftX opens a window per tool; a process-wide cache would hand one window's brush to
    /// another. A CodeView belongs to exactly one tree, so an instance field cannot.
    /// </para>
    /// </summary>
    private readonly Dictionary<(TokenKind Kind, bool Dark), SolidColorBrush> _foregroundBrushes = new();
    private readonly Dictionary<(TokenKind Kind, bool Dark), SolidColorBrush> _backgroundBrushes = new();

    private SolidColorBrush? ForegroundBrush(TokenKind kind, bool dark)
    {
        if (_foregroundBrushes.TryGetValue((kind, dark), out var cached))
            return cached;
        if (SyntaxPalette.ForegroundFor(kind, dark) is not { } color)
            return null;
        return _foregroundBrushes[(kind, dark)] = new SolidColorBrush(color);
    }

    private SolidColorBrush BackgroundBrush(TokenKind kind, bool dark)
    {
        if (_backgroundBrushes.TryGetValue((kind, dark), out var cached))
            return cached;
        return _backgroundBrushes[(kind, dark)] = new SolidColorBrush(SyntaxPalette.BackgroundFor(kind, dark));
    }

    public CodeView()
    {
        // No column definitions on the view itself: the gutter/content split belongs to the `row`
        // grid inside the scroller below. Declaring an Auto first column here as well left the
        // scroller sitting in it, so the whole view sized itself to the gutter instead of filling
        // the space it was given — the output pane never spanned its half of the window.
        // Horizontal scrolling is on because the content pane no longer wraps: with it Disabled a
        // line longer than the pane was clipped at the edge with no way to reach the rest of it.
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_gutter, 0);
        Grid.SetColumn(_content, 1);
        row.Children.Add(_gutter);
        row.Children.Add(_content);
        scroller.Content = row;
        Children.Add(scroller);

        // Decorative: the line numbers are a visual aid, and Narrator reading "one two three
        // four…" ahead of the document is noise, not information.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(
            _gutter, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        // On the content block, not this Grid: a UI test reads the result off the element that
        // holds the text.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_content, AutomationIds.EditorOutput);

        ActualThemeChanged += (_, _) => Render();
    }

    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set { _showLineNumbers = value; _gutter.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    }

    public string Text => _text;

    /// <summary>
    /// This view's accessible name, announced by Narrator when focus lands in its selectable text.
    /// A read-only colourised view carries no placeholder to fall back on at all, so without this
    /// it has no name whatsoever — see <see cref="CodeEditor.AccessibleName"/> for the input-side
    /// equivalent.
    /// </summary>
    public string AccessibleName
    {
        get => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(_content);
        set => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_content, value);
    }

    /// <summary>Sets the text and its tokens (empty tokens for plain/large-file display).</summary>
    public void SetContent(string text, IReadOnlyList<Token>? tokens = null)
    {
        _text = text ?? "";
        _tokens = _text.Length > MaxHighlightLength ? [] : tokens ?? [];
        Render();
    }

    public void Clear() => SetContent("");

    private void Render()
    {
        _content.Inlines.Clear();
        _content.TextHighlighters.Clear();

        int lineCount = 1;
        foreach (char c in _text)
            if (c == '\n')
                lineCount++;
        _gutter.Text = _showLineNumbers ? BuildGutter(lineCount) : "";

        if (_text.Length == 0)
            return;

        bool highContrast = HighContrast.IsActive();
        bool dark = ActualTheme != ElementTheme.Light;

        var highlightRanges = new Dictionary<TokenKind, List<TextRange>>();
        int cursor = 0;
        foreach (var token in _tokens.OrderBy(t => t.Start))
        {
            // Every token is clipped to the text and to what has not been emitted yet, rather than
            // trusted to be in range and disjoint. Producers are supposed to hand over a
            // non-overlapping cover (§10.1) and the ones here do — but this loop turns a token into
            // a run of text, so a token overlapping its neighbour silently prints those characters
            // twice, and one running past the end takes the window down on a Substring. Neither is
            // a failure a reader could diagnose from what they see, so neither is left possible.
            int start = Math.Max(token.Start, cursor);
            int end = Math.Min(token.End, _text.Length);
            if (end <= start)
                continue;

            if (start > cursor)
                _content.Inlines.Add(new Run { Text = _text[cursor..start] });

            var text = _text[start..end];
            if (!highContrast && SyntaxPalette.IsBackgroundKind(token.Kind))
            {
                _content.Inlines.Add(new Run { Text = text });
                if (!highlightRanges.TryGetValue(token.Kind, out var list))
                    highlightRanges[token.Kind] = list = [];
                list.Add(new TextRange { StartIndex = start, Length = end - start });
            }
            else
            {
                var run = new Run { Text = text };
                if (!highContrast && ForegroundBrush(token.Kind, dark) is { } brush)
                    run.Foreground = brush;
                _content.Inlines.Add(run);
            }
            cursor = end;
        }
        if (cursor < _text.Length)
            _content.Inlines.Add(new Run { Text = _text.Substring(cursor) });

        if (!highContrast)
        {
            foreach (var (kind, ranges) in highlightRanges)
            {
                var highlighter = new TextHighlighter { Background = BackgroundBrush(kind, dark) };
                foreach (var r in ranges)
                    highlighter.Ranges.Add(r);
                _content.TextHighlighters.Add(highlighter);
            }
        }
    }

    /// <summary>
    /// The line-number column: <c>1\n2\n…\nlineCount</c>.
    /// <para>
    /// Written out with a sized <see cref="System.Text.StringBuilder"/> rather than
    /// <c>string.Join('\n', Enumerable.Range(1, lineCount))</c>, which allocated an enumerator, a
    /// boxed <c>int</c> and a separate string per line before joining them — a hundred thousand
    /// throwaway objects on a large document, rebuilt on every repaint, for a column of digits.
    /// The capacity estimate assumes a worst-case seven digits plus a separator per line, so an
    /// ordinary document never grows the builder — but it is capped at a megabyte rather than
    /// taken literally, because the gutter is built for documents all the way up to the 50 MB
    /// file ceiling and reserving eight bytes per line for one of those would ask for hundreds of
    /// megabytes up front. Past the cap the builder grows the usual way, which is slower and
    /// bounded rather than fast and unbounded.
    /// </para>
    /// </summary>
    private static string BuildGutter(int lineCount)
    {
        int capacity = (int)Math.Min((long)lineCount * 8, 1L << 20);
        var sb = new System.Text.StringBuilder(capacity);
        for (int i = 1; i <= lineCount; i++)
        {
            if (i > 1)
                sb.Append('\n');
            sb.Append(i);
        }
        return sb.ToString();
    }
}

/// <summary>The theme-aware colour table for every <see cref="TokenKind"/> (design doc Appendix B).
/// Colour is never the only signal elsewhere in the app (errors also get a status-bar message,
/// diff lines a gutter mark, findings a badge) — this table supplies the colour half of that.
/// <para>
/// The syntax colours are Visual Studio Code's <b>Dark+</b> and <b>Light+</b> defaults, matched
/// hex for hex. They are the scheme the largest number of developers already read fluently, so a
/// JSON document here looks like the same document in their editor: keys light blue, string values
/// warm, numbers green, <c>true</c>/<c>false</c>/<c>null</c> blue. The previous table was GitHub's
/// palette, which is equally defensible but meant SwiftX agreed with neither the user's editor nor
/// itself.
/// </para></summary>
public static class SyntaxPalette
{
    public static bool IsBackgroundKind(TokenKind kind) =>
        kind is TokenKind.Match or TokenKind.Finding or TokenKind.Masked or TokenKind.Added or TokenKind.Removed;

    public static Color? ForegroundFor(TokenKind kind, bool dark) => kind switch
    {
        // JSON keys and XML attribute names — Dark+ variable blue / Light+ deep blue.
        TokenKind.PropertyName or TokenKind.AttributeName => dark ? C(0x9CDCFE) : C(0x0451A5),
        // String values, including XML attribute values.
        TokenKind.String or TokenKind.AttributeValue => dark ? C(0xCE9178) : C(0xA31515),
        TokenKind.Number => dark ? C(0xB5CEA8) : C(0x098658),
        // true / false / null, and any language keyword.
        TokenKind.Boolean or TokenKind.Null or TokenKind.Keyword => dark ? C(0x569CD6) : C(0x0000FF),
        TokenKind.TagName => dark ? C(0x569CD6) : C(0x800000),
        TokenKind.Comment or TokenKind.CData => dark ? C(0x6A9955) : C(0x008000),
        // Braces, brackets, commas and colons: dimmed rather than absent, so structure recedes
        // behind content instead of competing with it.
        TokenKind.Punctuation => dark ? C(0xD4D4D4) : C(0x3B3B3B),
        TokenKind.Error => dark ? C(0xF48771) : C(0xC42B1C),
        _ => null, // Plain: inherit the ambient text colour
    };

    public static Color BackgroundFor(TokenKind kind, bool dark) => kind switch
    {
        TokenKind.Match => dark ? C(0x5A4B00) : C(0xFFF3C4),
        TokenKind.Finding => dark ? C(0x5A2020) : C(0xFFE0E0),
        TokenKind.Masked => dark ? C(0x1F3055) : C(0xE0E8FF),
        TokenKind.Added => dark ? C(0x12341F) : C(0xDDF4E4),
        TokenKind.Removed => dark ? C(0x3F1A1D) : C(0xFBE3E4),
        _ => Colors.Transparent,
    };

    /// <summary>
    /// An opaque colour from a <c>0xRRGGBB</c> literal.
    /// <para>
    /// The literal is a number rather than the <c>"#RRGGBB"</c> string it used to be, because
    /// this is a hot path and the string form was not free: <c>TrimStart</c> plus three
    /// <c>Convert.ToByte(hex[..2], 16)</c> range slices allocated four strings and parsed six
    /// characters on <em>every call</em>, and the callers are the two render loops — CodeView
    /// building one <c>Run</c> per token, and CodeEditor colouring one document range per token
    /// on its 180 ms debounce, both over documents of up to 100 000 characters. Thousands of
    /// tokens therefore meant tens of thousands of throwaway strings per repaint, on the UI
    /// thread, to re-derive constants. As a <c>uint</c> the shift-and-mask is folded at compile
    /// time and the hex stays exactly as readable in source.
    /// </para>
    /// </summary>
    private static Color C(uint rgb) =>
        Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
