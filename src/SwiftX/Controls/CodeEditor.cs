using System.Globalization;
using System.Text;
using SwiftX.Services;
using SwiftX.Services.Syntax;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Core;
using VirtualKey = Windows.System.VirtualKey;

namespace SwiftX.Controls;

/// <summary>
/// The input side of an editor-shaped tool: a monospace editor with a line-number gutter and, when
/// a <see cref="Tokenizer"/> is set, live syntax colouring using the same
/// <see cref="SyntaxPalette"/> as the read-only <see cref="CodeView"/>.
/// <para>
/// <b>Why a RichEditBox.</b> A plain <see cref="TextBox"/> renders one uniform colour — there is no
/// per-run formatting — so the editable pane was necessarily monochrome. Since the formatter and
/// masker became single-pane tools, that meant no colour anywhere in them. A RichEditBox can
/// colour character ranges through its document object, which is the only way to get a coloured
/// <em>editable</em> surface in WinUI without hand-rolling a text control.
/// </para>
/// <para>
/// <b>Why it isn't the classic performance trap</b> (design doc §10.2): re-colouring is debounced
/// (<see cref="HighlightDelay"/>) so a burst of typing re-tokenizes once at the end rather than
/// per keystroke, and skipped entirely above <see cref="MaxHighlightLength"/>, where the document
/// falls back to plain monospace text. Both are the §22 large-input fallback the design document
/// prescribes.
/// </para>
/// </summary>
public sealed class CodeEditor : Grid
{
    /// <summary>Above this length the document is left uncoloured. Lower than
    /// <see cref="CodeView.MaxHighlightLength"/> because this side is re-coloured as the user
    /// types, not once per render.</summary>
    public const int MaxHighlightLength = 100_000;

    /// <summary>How long typing has to pause before the document is re-coloured.</summary>
    private static readonly TimeSpan HighlightDelay = TimeSpan.FromMilliseconds(180);

    public RichEditBox TextBox { get; } = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = DefaultEditorFontSize,
        BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        IsSpellCheckEnabled = false,
        VerticalContentAlignment = VerticalAlignment.Top,
        Padding = new Thickness(4, 2, 8, 8),
    };

    private readonly TextBlock _gutter = new()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = DefaultEditorFontSize,
        TextAlignment = TextAlignment.Right,
        Opacity = 0.45,
        Margin = new Thickness(8, 2, 8, 8),
        IsTextSelectionEnabled = false,
    };

    /// <summary>
    /// Wraps <see cref="_gutter"/> for hit-testing purposes only. A bare <see cref="TextBlock"/>
    /// has no <c>Background</c> property, and WinUI only hit-tests the blank space of an element
    /// that has one (even a fully transparent one) — most of the gutter's own area is exactly
    /// that blank space (padding, and every marker-less line's leading space), so a press
    /// anywhere but directly on a glyph would silently miss without this. <see cref="Gutter_PointerPressed"/>
    /// is wired here, not on <see cref="_gutter"/> itself, but still measures the press position
    /// against <c>_gutter</c>'s own bounds — <see cref="Microsoft.UI.Xaml.Input.PointerRoutedEventArgs.GetCurrentPoint"/>
    /// takes any element to measure against, independent of which one the handler is attached to.
    /// </summary>
    private readonly Border _gutterHitArea = new()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
    };

    private readonly DispatcherQueueTimer _highlightTimer;
    private bool _suppressTextChanged;

    public event EventHandler? TextChanged;

    /// <summary>
    /// Raised after a paste has actually landed in the document — one dispatcher cycle later,
    /// since <see cref="RichEditBox.Paste"/> itself fires before the text is inserted (that's
    /// what lets a handler override the operation). A page that wants to auto-format pasted
    /// content, rather than content the user is still typing, listens here instead of debouncing
    /// every <see cref="TextChanged"/>.
    /// </summary>
    public event EventHandler? Pasted;

    /// <summary>The tokenizer to colour with, or null to leave the text plain. Setting it
    /// re-colours what is already in the editor.</summary>
    public ITokenizer? Tokenizer
    {
        get => _tokenizer;
        set
        {
            if (ReferenceEquals(_tokenizer, value))
                return;
            _tokenizer = value;
            Highlight();
        }
    }

    private ITokenizer? _tokenizer;

    /// <summary>A single-character marker shown before a line's number in the gutter, with the
    /// action a click on that line runs. A plain text character, not an icon-font glyph — the
    /// gutter renders in <see cref="TextBox"/>'s own code font, not Segoe Fluent Icons.</summary>
    public readonly record struct GutterMarker(char Symbol, Action OnClick);

    /// <summary>
    /// Optional per-display-line markers, keyed by 1-based line number. Null or empty leaves the
    /// gutter as plain right-aligned numbers (the common case, for every tool that never sets
    /// this); setting it rebuilds the gutter immediately. Every line gets the same fixed-width
    /// "marker-or-space, then the number" prefix once any marker is present, so numbers stay in
    /// one column regardless of which lines actually carry a marker.
    /// <para>
    /// A caller that owns fold/outline state (see <c>FormatterPage</c>) is the intended user:
    /// this control only renders the marker and routes the click back to
    /// <see cref="GutterMarker.OnClick"/> — it has no idea what a marker means.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<int, GutterMarker>? GutterMarkers
    {
        get => _gutterMarkers;
        set
        {
            _gutterMarkers = value;
            RefreshGutter();
        }
    }

    private IReadOnlyDictionary<int, GutterMarker>? _gutterMarkers;

    /// <summary>The line count the gutter currently shows, or -1 when it shows nothing yet — see
    /// <see cref="RefreshGutter"/>.</summary>
    private int _gutterLineCount = -1;

    /// <summary>Whether what the gutter currently shows was built from markers, so a document that
    /// merely kept its line count while its markers came or went is still rebuilt.</summary>
    private bool _gutterHasMarkers;

    public string PlaceholderText
    {
        get => TextBox.PlaceholderText;
        set => TextBox.PlaceholderText = value;
    }

    /// <summary>
    /// The editor's accessible name, announced by Narrator when focus lands in it. Distinct from
    /// <see cref="PlaceholderText"/>, which is example content (and often absent, or a syntax
    /// sample like a JWT shape) rather than a name for the field — a two-pane tool needs its input
    /// and output distinguishable by name alone, not by whichever example text happens to be set.
    /// </summary>
    public string AccessibleName
    {
        get => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(TextBox);
        set => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TextBox, value);
    }

    // ---- Zoom -----------------------------------------------------------------------------
    // Ctrl+(+/-) and Ctrl+scroll, the same two triggers every text-heavy editor (browsers, VS
    // Code, ...) already binds zoom to — so this needs no discovery, no on-screen control.

    private const double DefaultEditorFontSize = 13;
    private const double MinZoomFontSize = 8;
    private const double MaxZoomFontSize = 32;
    private const double ZoomStep = 1;

    /// <summary>Text size for both the document and its line-number gutter — kept as one value
    /// so the two never drift out of vertical alignment with each other.</summary>
    public double EditorFontSize
    {
        get => TextBox.FontSize;
        set
        {
            double clamped = Math.Clamp(value, MinZoomFontSize, MaxZoomFontSize);
            TextBox.FontSize = clamped;
            _gutter.FontSize = clamped;
        }
    }

    public void ZoomIn() => EditorFontSize += ZoomStep;

    public void ZoomOut() => EditorFontSize -= ZoomStep;

    /// <summary>
    /// Ctrl+scroll zooms instead of scrolling — handled here, on the editor's own content, so it
    /// fires (and marks the wheel event Handled) during the bubble phase before it ever reaches
    /// the wrapping <see cref="ScrollViewer"/> in the constructor, which is what stops the
    /// ScrollViewer's own built-in wheel scrolling from running underneath the zoom.
    /// </summary>
    private void Content_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!IsCtrlDown())
            return;

        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta > 0)
            ZoomIn();
        else if (delta < 0)
            ZoomOut();
        e.Handled = true;
    }

    /// <summary>
    /// Ctrl+Plus/Minus, on both the number row and the numpad. Wired to <c>PreviewKeyDown</c> in
    /// the constructor, tunnelling past the RichEditBox's own key handling — the same reason
    /// <c>EditorToolPage.InitializeChrome</c>'s tunnelling exists: caught on the bubble instead,
    /// "+"/"-" would already have been typed into the document before this ever saw the key.
    /// </summary>
    private void CodeEditor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsCtrlDown())
            return;

        // VirtualKey has no OemPlus/OemMinus members — those OEM keys were never added to the
        // WinRT enum — so the main-row +/- are matched by their raw Win32 virtual-key codes
        // (VK_OEM_PLUS/VK_OEM_MINUS) alongside the numpad's own named Add/Subtract.
        const int VkOemPlus = 0xBB;
        const int VkOemMinus = 0xBD;

        switch ((int)e.Key)
        {
            case VkOemPlus:
            case (int)VirtualKey.Add:
                ZoomIn();
                e.Handled = true;
                break;
            case VkOemMinus:
            case (int)VirtualKey.Subtract:
                ZoomOut();
                e.Handled = true;
                break;
        }
    }

    private static bool IsCtrlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>
    /// The last value <see cref="Text"/> read out of the document, or null when the document has
    /// changed since. See <see cref="Text"/> for why this exists.
    /// </summary>
    private string? _cachedText;

    /// <summary>
    /// The document's text, with the rich-edit paragraph mark (<c>\r</c>) normalised to
    /// <c>\n</c>. The substitution is one character for one character, so a token offset computed
    /// over this string indexes the same position in the underlying document — which is what makes
    /// colouring by range possible at all.
    /// <para>
    /// <b>Memoized between changes</b>, because reading it is not cheap and the callers read it
    /// several times per change: one <c>GetText</c> copies the whole document out of the native
    /// rich-edit store, and the <c>Replace</c> copies it again. A tool page's own
    /// <c>TextChanged</c> handler reads this three or four times in a row (is it empty, is it
    /// dirty, count the characters, re-detect the format), <see cref="LineCount"/> read the
    /// document a fifth time for the gutter, and <see cref="Highlight"/> a sixth — so a single
    /// keystroke in a one-megabyte document allocated megabytes, on the UI thread, to re-derive
    /// the same string six times. The cache is dropped by the <c>TextChanged</c> handler below
    /// (before its own early-out, so a suppressed change invalidates it too) and by the setter, so
    /// it can never outlive the document it was read from.
    /// </para>
    /// </summary>
    public string Text
    {
        get => _cachedText ??= ReadDocumentText();
        set
        {
            _suppressTextChanged = true;
            try
            {
                TextBox.Document.SetText(TextSetOptions.None, value ?? "");
            }
            finally
            {
                _suppressTextChanged = false;
            }
            _cachedText = null;
            RefreshGutter();
            Highlight();
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Whether the document is empty — the one question the tool pages ask of <see cref="Text"/>
    /// most often, spelled out so the intent at those call sites is "is there anything here" rather
    /// than a length comparison that reads like it might be free.
    /// </summary>
    public bool IsEmpty => Text.Length == 0;

    private string ReadDocumentText()
    {
        TextBox.Document.GetText(TextGetOptions.None, out var text);
        // The document always reports a trailing paragraph mark that the user did not type.
        if (text.EndsWith('\r'))
            text = text[..^1];
        return text.Replace('\r', '\n');
    }

    /// <summary>
    /// Sets the text and the tokenizer that colours it in one step, colouring the result once.
    /// <para>
    /// Assigning the two properties one after the other colours the whole document twice: the
    /// <see cref="Text"/> setter colours it with the tokenizer still left over from the previous
    /// document, and the <see cref="Tokenizer"/> setter then colours it again. That pass is the
    /// expensive one on this control — it walks every token and crosses the rich-edit ABI twice per
    /// token to set a range's colour — and for a page that swaps both at once (the format it
    /// detected decides the tokenizer, so every load does) the first of the two was always wasted
    /// work against the wrong palette.
    /// </para>
    /// </summary>
    public void SetContent(string text, ITokenizer? tokenizer)
    {
        _tokenizer = tokenizer;
        Text = text;
    }

    public CodeEditor()
    {
        // The editor's text does not wrap (a code editor that reflows a long JSON line is unusable
        // for finding a column number), so the scroller has to be able to scroll sideways —
        // Disabled meant anything past the right edge was clipped and unreachable.
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _gutterHitArea.Child = _gutter;
        Grid.SetColumn(_gutterHitArea, 0);
        Grid.SetColumn(TextBox, 1);
        row.Children.Add(_gutterHitArea);
        row.Children.Add(TextBox);
        scroller.Content = row;
        Children.Add(scroller);

        // Wired on the content row, not the ScrollViewer itself: the wheel event bubbles from
        // whichever of the gutter or the RichEditBox the pointer is actually over, up through
        // this row, and only then to the ScrollViewer above it — so marking it Handled here (see
        // Content_PointerWheelChanged) reaches the ScrollViewer's own built-in wheel scrolling
        // before it runs, regardless of which of the two the pointer was over.
        row.PointerWheelChanged += Content_PointerWheelChanged;
        PreviewKeyDown += CodeEditor_PreviewKeyDown;

        // Decorative: the line numbers are a visual aid, and Narrator reading "one two three
        // four…" ahead of the document is noise, not information.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(
            _gutter, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        // On the RichEditBox rather than on this Grid: the id has to land on the control that
        // actually carries the text pattern, which is what a UI test types into.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(TextBox, AutomationIds.EditorInput);

        // RichEditBox is a rich-text control being used as a code editor: everything that would
        // "help" a document — smart quotes, auto-bulleting, spell check — corrupts code.
        TextBox.ClipboardCopyFormat = RichEditClipboardFormat.PlainText;
        TextBox.DisabledFormattingAccelerators = DisabledFormattingAccelerators.All;
        TextBox.Document.UndoLimit = 100;

        _highlightTimer = DispatcherQueue.CreateTimer();
        _highlightTimer.IsRepeating = false;
        _highlightTimer.Interval = HighlightDelay;
        _highlightTimer.Tick += (_, _) => Highlight();

        // A pending highlight outlives the window that owns it. The dispatcher holds a running
        // timer, the timer's Tick closure holds this editor, and the editor holds the document —
        // so between the last keystroke and the tick 180ms later, closing the window leaves a
        // colouring pass queued against a RichEditBox whose native document has gone. Highlight()
        // guards its tokenizing but reads the selection before that guard opens, so the throw
        // lands on a timer tick with no caller: an unhandled exception, not a mis-coloured line.
        Unloaded += (_, _) => _highlightTimer.Stop();

        TextBox.TextChanged += (_, _) =>
        {
            // Before the early-out, not after: a suppressed change is still a change to the
            // document, so a cached copy read before it is stale either way.
            _cachedText = null;
            if (_suppressTextChanged)
                return;
            RefreshGutter();
            _highlightTimer.Start(); // restarts the countdown on every keystroke
            TextChanged?.Invoke(this, EventArgs.Empty);
        };

        // Deferred past the paste itself: at the point Paste fires the clipboard content hasn't
        // been inserted yet (that's what would let a handler override the operation), so reading
        // Text here would still see the pre-paste document.
        TextBox.Paste += (_, _) => DispatcherQueue.TryEnqueue(() => Pasted?.Invoke(this, EventArgs.Empty));

        ActualThemeChanged += (_, _) => Highlight();
        _gutterHitArea.PointerPressed += Gutter_PointerPressed;
        RefreshGutter();
    }

    /// <summary>
    /// The number of display lines in the document.
    /// <para>
    /// This runs on <em>every keystroke</em>, from the <c>TextChanged</c> handler, so what it
    /// allocates is paid per character typed — and note that <see cref="MaxHighlightLength"/> does
    /// not bound it: that gates colouring only, while the gutter is rebuilt for documents all the
    /// way up to <c>InputLimits.MaxTextBytes</c>. It counts over <see cref="Text"/>, which is
    /// memoized between changes (see that property), so the one document read a keystroke needs is
    /// shared with every other caller in the same change rather than being a read of its own: this
    /// used to call <c>GetText</c> directly precisely to avoid a second copy, which stopped being
    /// the cheaper of the two the moment the copy it was avoiding became free.
    /// </para>
    /// </summary>
    private int LineCount()
    {
        int lines = 1;
        foreach (char c in Text)
            if (c == '\n')
                lines++;
        return lines;
    }

    /// <summary>
    /// Rebuilds the line-number column — but only when it would actually come out different.
    /// <para>
    /// This is called on every keystroke, and the overwhelming majority of keystrokes do not add or
    /// remove a line, so the column they produce is character for character the one already on
    /// screen. Rebuilding it anyway meant a fresh multi-line string per character typed: on a
    /// hundred-thousand-line document that is close to a megabyte of garbage, plus a
    /// <see cref="TextBlock"/> re-layout, to redraw digits that did not move. The numbers depend on
    /// nothing but the line count, so the count is what is compared.
    /// </para>
    /// <para>
    /// The marker path is exempt from the skip and always rebuilds: markers are assigned as a whole
    /// dictionary (see <see cref="GutterMarkers"/>) and can change while the line count does not,
    /// so "same number of lines" is not evidence that the same column would come out.
    /// </para>
    /// </summary>
    private void RefreshGutter()
    {
        int lines = LineCount();

        if (_gutterMarkers is not { Count: > 0 } markers)
        {
            if (!_gutterHasMarkers && _gutterLineCount == lines)
                return;
            _gutterLineCount = lines;
            _gutterHasMarkers = false;
            _gutter.Text = BuildNumberColumn(lines);
            return;
        }

        _gutterLineCount = lines;
        _gutterHasMarkers = true;

        // Every line gets the same "marker-or-space, space, right-padded number" prefix once any
        // marker exists, so the number column stays put regardless of which lines have one — the
        // per-line lengths are then all equal, which is also why TextAlignment.Right above never
        // needs special-casing this path.
        int width = lines.ToString(CultureInfo.InvariantCulture).Length;
        var sb = new StringBuilder(GutterCapacityFor(lines, width + 2));
        for (int line = 1; line <= lines; line++)
        {
            if (line > 1)
                sb.Append('\n');
            char marker = markers.TryGetValue(line, out var m) ? m.Symbol : ' ';
            sb.Append(marker).Append(' ').Append(line.ToString(CultureInfo.InvariantCulture).PadLeft(width));
        }
        _gutter.Text = sb.ToString();
    }

    /// <summary>
    /// <c>1\n2\n…\nlines</c>, written with a sized <see cref="StringBuilder"/> rather than
    /// <c>string.Join('\n', Enumerable.Range(1, lines))</c> — the same change, and for the same
    /// reason, as <c>CodeView.BuildGutter</c>'s: that expression allocates an enumerator, a
    /// boxed <c>int</c> and a separate string per line before joining them, which is a hundred
    /// thousand throwaway objects on a large document for a column of digits. It matters more here
    /// than it does there, because this side is rebuilt as the user types rather than once per
    /// render.
    /// </summary>
    private static string BuildNumberColumn(int lines)
    {
        var sb = new StringBuilder(GutterCapacityFor(lines, 8));
        for (int line = 1; line <= lines; line++)
        {
            if (line > 1)
                sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString();
    }

    /// <summary>
    /// A starting capacity for the gutter string, capped rather than taken literally: the gutter is
    /// built for documents all the way up to the 50 MB file ceiling, and reserving a per-line
    /// estimate for one of those would ask for hundreds of megabytes up front. Past the cap the
    /// builder grows the usual way, which is slower and bounded rather than fast and unbounded.
    /// </summary>
    private static int GutterCapacityFor(int lines, int perLine) =>
        (int)Math.Min((long)lines * perLine, 1L << 20);

    /// <summary>
    /// Maps a press on the gutter to the display line under it and, if that line carries a
    /// marker, runs its click action. The line-height-from-<see cref="FrameworkElement.ActualHeight"/>
    /// division below is only ever evaluated in response to an actual press — by which point the
    /// gutter has necessarily already been laid out at least once — so unlike computing it
    /// eagerly right after a text change, there is no stale-layout race here.
    /// </summary>
    private void Gutter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_gutterMarkers is not { Count: > 0 } markers || _gutter.ActualHeight <= 0)
            return;

        int lines = LineCount();

        double y = e.GetCurrentPoint(_gutter).Position.Y;
        int line = 1 + (int)(y / (_gutter.ActualHeight / lines));
        if (markers.TryGetValue(line, out var marker))
        {
            marker.OnClick();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Re-colours the whole document from the current tokenizer. Runs inside a display-update
    /// batch so the user never sees the intermediate reset-to-default, and saves and restores the
    /// selection because setting character formatting moves the insertion point.
    /// </summary>
    private void Highlight()
    {
        _highlightTimer.Stop();

        // Under High Contrast the platform's own TextControlForeground resource already carries
        // the palette the user picked (design doc §13.3: "High Contrast always wins") — forcing a
        // hardcoded Black/White here would fight it, exactly the bug CodeView's own render path
        // already guards against for the read-only side. There is no "clear to ambient" for a
        // RichEditBox's character-level ForegroundColor the way there is for a TextBlock brush, so
        // the ambient colour has to be read back from the control's own (theme-resolved) Foreground
        // and reapplied explicitly instead.
        bool highContrast = HighContrast.IsActive();
        var defaultColor = highContrast
            ? (TextBox.Foreground as SolidColorBrush)?.Color
            : ThemeHelper.ColorFor(this, "SwiftXForegroundColor");

        string text;
        int selectionStart;
        int selectionEnd;
        try
        {
            // Reading the document, reading the selection and opening the batch are inside a guard
            // of their own, not above the main one. Every line here talks to the RichEditBox's
            // native document, and the one caller is a timer tick — so on the closed-window path
            // (see the Unloaded handler in the constructor) these are the calls that throw, and
            // they used to throw from outside every try in this method.
            //
            // Reading the text is one of them, and was the line still left outside: Unloaded is
            // what stops the pending tick, and it is not guaranteed to run when a window closes
            // with the tree still attached — so a paste followed immediately by closing the window
            // could still land here with the native document gone. An exception on a timer tick has
            // no caller, which makes it an unhandled one: the process, not the colouring, is what
            // ends.
            text = Text;
            selectionStart = TextBox.Document.Selection.StartPosition;
            selectionEnd = TextBox.Document.Selection.EndPosition;
            TextBox.Document.BatchDisplayUpdates();
        }
        catch (Exception ex)
        {
            Services.Diag.Log("CodeEditor.Highlight: document unavailable: " + ex.Message);
            return;
        }

        _suppressTextChanged = true;
        try
        {
            // Tokenizing is inside the guard, not before it. This runs on whatever the user has
            // pasted, on a timer tick — and an exception on a timer tick has no caller to catch it,
            // so a scanner that walked off the end of a half-typed document would take the process
            // down rather than mis-colour a line. (The scanners are fuzzed against truncated and
            // mutated input in TokenizerFuzzTests; this is the belt to that pair of braces.)
            IReadOnlyList<Token> tokens = !highContrast && Tokenizer is { } tokenizer && text.Length is > 0 and <= MaxHighlightLength
                ? tokenizer.Tokenize(text)
                : [];

            // One reset, then one range per token: the gaps between tokens keep the default
            // colour rather than needing a range of their own. If the ambient colour could not be
            // resolved to a concrete Color (unexpected, but Foreground is a Brush, not always a
            // SolidColorBrush), the existing formatting is left alone rather than guessed at.
            if (defaultColor is { } resolvedDefault)
                TextBox.Document.GetRange(0, text.Length + 1).CharacterFormat.ForegroundColor = resolvedDefault;

            if (!highContrast)
            {
                bool dark = ActualTheme != ElementTheme.Light;
                foreach (var token in tokens)
                {
                    if (SyntaxPalette.ForegroundFor(token.Kind, dark) is not { } color)
                        continue;
                    TextBox.Document.GetRange(token.Start, token.End).CharacterFormat.ForegroundColor = color;
                }
            }
        }
        catch (Exception ex)
        {
            // A malformed range must never take the window down — the worst case here is
            // uncoloured text, which is exactly what the large-document path already shows.
            Services.Diag.Log("CodeEditor.Highlight failed: " + ex.Message);
        }
        finally
        {
            // Guarded too: a finally that throws replaces the exception the catch above just
            // handled with a new one, and this one runs on that same timer tick.
            try
            {
                TextBox.Document.Selection.SetRange(selectionStart, selectionEnd);
                TextBox.Document.ApplyDisplayUpdates();
            }
            catch (Exception ex)
            {
                Services.Diag.Log("CodeEditor.Highlight: could not restore the document: " + ex.Message);
            }
            _suppressTextChanged = false;
        }
    }

    public void Focus() => TextBox.Focus(FocusState.Programmatic);
}
