using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using SwiftX.Services.Formats;
using SwiftX.Services.Syntax;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace SwiftX.ToolPages;

/// <summary>
/// Backs the one Data Formatter catalog entry: auto-detects JSON or XML and formats it.
/// (JSON and XML used to be separate catalog entries, each this same page pinned to a fixed
/// format — Phase 1 shipped them that way, but a fixed format was never more than "Auto, minus
/// the detection step", so they were merged into this single auto-detecting tool.) Basic chrome
/// stays lean (Format / Validate / Copy / Clear); Advanced opens a sidebar for minify, sort,
/// escape, convert and indent. Invalid input never clears the user's text.
/// </summary>
public sealed class FormatterPage : EditorToolPage
{
    private const int DefaultIndentWidth = 2;
    private const double IndentBoxWidth = 60;
    private const double AdvancedSidebarWidth = 150;

    // Built from the codepoint, not a literal character in source, so the glyph can't be
    // silently swapped for a different (or invisible) private-use character by a stray
    // copy/paste — see the memory note on this exact failure mode. "LightningBolt" — the
    // Advanced toggle is icon-only now, so this is the one place it appears.
    private static readonly string AdvancedGlyph = char.ConvertFromUtf32(0xE945);
    private static readonly string SaveGlyph = char.ConvertFromUtf32(0xE74E);
    private static readonly string SearchGlyph = char.ConvertFromUtf32(0xE721);
    private static readonly string SearchCloseGlyph = char.ConvertFromUtf32(0xE711);
    private static readonly string SearchPrevGlyph = char.ConvertFromUtf32(0xE70E);
    private static readonly string SearchNextGlyph = char.ConvertFromUtf32(0xE70D);
    private static readonly string OutlineGlyph = char.ConvertFromUtf32(0xE70D);

    private readonly CodeEditor _editor = new();
    private readonly NumberBox _indentBox = new()
    {
        Value = DefaultIndentWidth,
        Minimum = 1,
        Maximum = 8,
        SmallChange = 1,
        LargeChange = 2,
        Width = IndentBoxWidth,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Border _advancedPanel;
    private readonly ToggleButton _advancedToggle;
    private readonly Button _saveButton;
    private readonly FrameworkElement _headerActions;
    private readonly TextBlock _detectedLabel = new()
    {
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Opacity = 0.75,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ---- Search --------------------------------------------------------------------------

    private readonly Border _searchPanel;
    private readonly TextBox _searchBox = new()
    {
        Width = 130,
        Height = 28,
        Padding = new Thickness(8, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _searchCountLabel = new()
    {
        Opacity = 0.65,
        MinWidth = 34,
        FontSize = 11,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 0, 0),
    };

    /// <summary>Start offset of every non-overlapping match for the current search box text, in
    /// <see cref="CodeEditor.Text"/> order. Recomputed on every search-box keystroke and on every
    /// editor text change while the panel is open (see <see cref="RefreshSearchMatches"/>) rather
    /// than incrementally patched — the document is small enough (Formatter's own
    /// <see cref="CodeEditor.MaxHighlightLength"/> already bounds it) that a full rescan costs
    /// nothing, and it's the only way to stay correct after an edit changes offsets.</summary>
    private readonly List<int> _searchMatches = new();
    private int _searchCurrentMatch = -1;

    // ---- Outline (collapse/expand) --------------------------------------------------------
    // See EnterOutlineMode's doc comment for the overall safety model: the RichEditBox has no
    // native concept of folded/hidden text, so this reads real edits back out of the document by
    // making it read-only for the whole time anything is collapsed, and unconditionally restores
    // the frozen original text before any command that acts on the document's real content.

    private const char FoldCollapsedSymbol = '▸';
    private const char FoldExpandedSymbol = '▾';

    /// <summary>The real, complete document as it stood the moment outline mode was entered, or
    /// null when it isn't active. Never mutated by a fold toggle — folding only ever changes what
    /// <see cref="_editor"/> displays, never this.</summary>
    private string? _trueText;

    private List<FoldRegion> _foldRegions = new();

    /// <summary>Which regions (by <see cref="FoldRegion.Start"/>, unique per region within one
    /// frozen <see cref="_trueText"/>) are currently collapsed.</summary>
    private readonly HashSet<int> _collapsedStarts = new();

    private bool _outlineActive;

    private IReadOnlyList<ToolCommand>? _commands;
    private IDataFormat? _activeFormat;
    private bool _isDirty;
    private bool _replacing;
    private readonly DispatcherQueueTimer _detectTimer;

    public FormatterPage(OrbitManager manager) : base(manager)
    {
        _editor.PlaceholderText = Loc.Get("Formatter.InputPlaceholder");
        _editor.AccessibleName = Loc.Get("Common.Input");

        _detectTimer = DispatcherQueue.CreateTimer();
        _detectTimer.Interval = TimeSpan.FromMilliseconds(280);
        _detectTimer.IsRepeating = false;
        _detectTimer.Tick += (_, _) => RefreshDetection(autoValidate: true);

        // Built before the TextChanged handler below, which reads its Visibility.
        _searchPanel = BuildSearchPanel();

        // The instant the user clicks or tabs into the editor to actually work with it, drop any
        // outline session — see EnterOutlineMode's doc comment for why this, rather than making
        // the RichEditBox read-only, is what keeps folding from ever costing a keystroke: GotFocus
        // fires before any input can land, so the full text is always back in place first.
        _editor.TextBox.GotFocus += (_, _) => ExitOutlineMode();

        _editor.TextChanged += (_, _) =>
        {
            // Outside the _replacing guard below: the search button's visibility and an open
            // search panel's matches have to track the document through every source of change —
            // typing, Format, Minify, Convert, Clear — not just the ones that skip StatusBar's
            // own counting because they already did it themselves.
            RefreshCommandVisibility();
            if (_editor.Text.Length == 0)
                CloseSearch();
            else if (_searchPanel.Visibility == Visibility.Visible)
                RefreshSearchMatches();

            if (_replacing)
                return;
            _isDirty = _editor.Text.Length > 0;
            StatusBar.SetCounts(_editor.Text);
            _detectTimer.Stop();
            _detectTimer.Start();
        };

        // A paste is a deliberate "here's a document" moment, unlike the keystrokes TextChanged
        // debounces above — so it gets the full clean+format+detect pass immediately, the same
        // one the orbit's "paste clipboard" chip already triggers via PasteClipboardText.
        _editor.Pasted += (_, _) => ValidateAndClean();

        ToolTipService.SetToolTip(_indentBox, Loc.Get("Formatter.Indent"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_indentBox, Loc.Get("Formatter.Indent"));
        _indentBox.EnableWheelStep(1);

        _advancedToggle = new ToggleButton
        {
            Content = new FontIcon { Glyph = AdvancedGlyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 4, 8, 4),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_advancedToggle, ToolCommand.Ids.Advanced);
        ToolTipService.SetToolTip(_advancedToggle, Loc.Get("Tool.Advanced"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_advancedToggle, Loc.Get("Tool.Advanced"));

        _advancedPanel = BuildAdvancedSidebar();
        _advancedToggle.Checked += (_, _) => _advancedPanel.Visibility = Visibility.Visible;
        _advancedToggle.Unchecked += (_, _) => _advancedPanel.Visibility = Visibility.Collapsed;

        _saveButton = new Button
        {
            Content = new FontIcon { Glyph = SaveGlyph, FontSize = 14 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(_saveButton, Loc.Get("Tool.Save"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_saveButton, Loc.Get("Tool.Save"));
        _saveButton.Click += (_, _) => _ = SaveToFileAsync();
        ApplyHoverIconColor(_saveButton, HoverAccent.Accent);

        _headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { _advancedToggle, _saveButton },
        };

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var editorPane = Pane(_editor);
        // The search panel overlays only the editor's own column — not the Advanced sidebar next
        // to it — so it stays anchored to "top right corner of the text", not the window.
        var editorArea = new Grid();
        editorArea.Children.Add(editorPane);
        editorArea.Children.Add(_searchPanel);
        Grid.SetColumn(editorArea, 0);
        Grid.SetColumn(_advancedPanel, 1);
        body.Children.Add(editorArea);
        body.Children.Add(_advancedPanel);

        SetBody(body);
        SetFooter(_detectedLabel);
        StatusBar.SetUntouched();
        InitializeChrome();
        RefreshDetection(autoValidate: false);
    }

    /// <summary>
    /// Repaints the search panel and Advanced sidebar — both built once at construction and kept
    /// around for the life of the page (shown and hidden by <c>Visibility</c>, never rebuilt), so
    /// unlike a per-row or per-card view neither one is naturally revisited by anything else a
    /// theme change already triggers.
    /// </summary>
    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _searchPanel.Background = ThemeBrush("ShellCardBackgroundBrush");
        _searchPanel.BorderBrush = ThemeBrush("ShellCardBorderBrush");
        _advancedPanel.Background = ThemeBrush("ShellPillBrush");
        _advancedPanel.BorderBrush = ThemeBrush("ShellCardBorderBrush");
        ApplyHoverIconColor(_saveButton, HoverAccent.Accent);
    }

    public override ToolKind Kind => ToolKind.Data;
    public override bool IsDirty => _isDirty;
    public override bool CanSave => true;

    /// <summary>The Advanced toggle and Save live in the title bar, next to minimize/maximize,
    /// rather than the command bar — important enough to stay reachable without opening it.</summary>
    public override FrameworkElement? HeaderActions => _headerActions;

    public override Task<bool> SaveAsync() => SaveToFileAsync();

    /// <summary>Basic bar only — Format, Validate, Search, Copy, Clear. Advanced lives in the
    /// sidebar. Search sits between Validate and Copy and only shows up once there's something to
    /// search — <see cref="ToolCommand.IsVisible"/>, refreshed by
    /// <see cref="EditorToolPage.RefreshCommandVisibility"/> on every text change — since it has
    /// nothing to do against an empty document.</summary>
    public override IReadOnlyList<ToolCommand> Commands => _commands ??=
    [
        new ToolCommand(Loc.Get("Tool.Format"), "\uE8E3", Format, VirtualKey.Enter, VirtualKeyModifiers.Control, id: ToolCommand.Ids.Format),
        ToolCommand.Validate(ValidateAndClean),
        new ToolCommand(
            Loc.Get("Formatter.Search"), SearchGlyph, OpenSearch,
            VirtualKey.F, VirtualKeyModifiers.Control,
            canExecute: () => _editor.Text.Length > 0,
            id: ToolCommand.Ids.Search)
        {
            IconOnly = true,
            IsVisible = () => _editor.Text.Length > 0,
        },
        ToolCommand.Copy(CopyOutput),
        ToolCommand.Clear(Clear),
    ];

    public override bool AcceptsClipboardText(string text) =>
        text.Length > 0 && FormatRegistry.DetectBest(text).DetectConfidence(text) > 0;

    public override void PasteClipboardText(string text)
    {
        ExitOutlineMode();
        _editor.Text = text;
        ValidateAndClean();
    }

    public override async Task LoadFileAsync(string path)
    {
        var read = await InputLimits.ReadTextAsync(path);
        if (!read.Ok)
        {
            StatusBar.SetMessage(read.Error, isError: true);
            return;
        }

        ExitOutlineMode();
        _editor.Text = read.Text;
        ValidateAndClean();
    }

    private FormatOptions Options => new()
    {
        IndentWidth = double.IsNaN(_indentBox.Value) ? DefaultIndentWidth : (int)_indentBox.Value,
        SortKeys = false,
    };

    private void Replace(string text)
    {
        _replacing = true;
        try
        {
            _editor.Text = text;
        }
        finally
        {
            _replacing = false;
        }
        StatusBar.SetCounts(text);
        RefreshDetection(autoValidate: false);
    }

    private void Format()
    {
        ExitOutlineMode();
        var text = _editor.Text;
        if (text.Length == 0)
        {
            StatusBar.SetUntouched();
            return;
        }

        SetActiveFormat(FormatRegistry.DetectBest(text));
        var result = _activeFormat!.Format(text, Options);
        if (!result.Success)
        {
            var d = result.Diagnostics[0];
            StatusBar.SetError(d.Line, d.Column);
            return;
        }

        Replace(result.Text);
        StatusBar.SetValid();
    }

    /// <summary>
    /// Clean messy paste wrappers (outer quotes/spaces/escapes), then format with a fixed 2-space
    /// indent and report validity. Primary "make this usable" action for log-copied payloads.
    /// </summary>
    private void ValidateAndClean()
    {
        ExitOutlineMode();
        var text = _editor.Text;
        if (text.Length == 0)
        {
            StatusBar.SetUntouched();
            return;
        }

        var cleaned = InputSanitizer.Clean(text);
        var format = FormatRegistry.DetectBest(cleaned);
        var formatOptions = new FormatOptions { IndentWidth = DefaultIndentWidth, SortKeys = false };
        var result = format.Format(cleaned, formatOptions);
        if (!result.Success)
        {
            if (!string.Equals(cleaned, text, StringComparison.Ordinal))
                Replace(cleaned);
            SetActiveFormat(format);
            var d = result.Diagnostics[0];
            StatusBar.SetError(d.Line, d.Column);
            return;
        }

        Replace(result.Text);
        SetActiveFormat(format);
        StatusBar.SetValid();
    }

    private void SetActiveFormat(IDataFormat format)
    {
        _activeFormat = format;
        _editor.Tokenizer = format.Tokenizer;
        UpdateDetectedLabel(format);
    }

    private void RefreshDetection(bool autoValidate)
    {
        var text = _editor.Text;
        if (text.Length == 0)
        {
            _detectedLabel.Text = "";
            if (!autoValidate)
                StatusBar.SetUntouched();
            return;
        }

        var format = FormatRegistry.DetectBest(text);
        UpdateDetectedLabel(format);
        _editor.Tokenizer = format.Tokenizer;

        if (!autoValidate)
            return;

        var diagnostics = format.Validate(text);
        if (diagnostics.Count == 0)
            StatusBar.SetValid();
        else
            StatusBar.SetError(diagnostics[0].Line, diagnostics[0].Column);
    }

    private void UpdateDetectedLabel(IDataFormat format) =>
        _detectedLabel.Text = Loc.Format("Formatter.FormatName", format.Id.ToUpperInvariant());

    private void Minify()
    {
        ExitOutlineMode();
        var text = _editor.Text;
        if (text.Length == 0)
            return;
        SetActiveFormat(FormatRegistry.DetectBest(text));
        var result = _activeFormat!.Minify(text);
        if (!result.Success)
        {
            var d = result.Diagnostics[0];
            StatusBar.SetError(d.Line, d.Column);
            return;
        }
        Replace(result.Text);
        StatusBar.SetValid();
    }

    private void SortKeys()
    {
        ExitOutlineMode();
        var text = _editor.Text;
        if (text.Length == 0)
            return;

        var format = FormatRegistry.DetectBest(text);
        try
        {
            var node = format.ToCanonical(text);
            var options = Options with { SortKeys = true };
            // Key sorting is a JSON-shaped operation: rewrite through the JSON writer, then leave
            // the document as JSON (or re-emit via the source format when it was already JSON).
            string sortedJson = FormatRegistry.Json.FromCanonical(node, options);
            if (format.Id == "json")
            {
                Replace(sortedJson);
                SetActiveFormat(format);
            }
            else
            {
                Replace(sortedJson);
                SetActiveFormat(FormatRegistry.Json);
            }
            StatusBar.SetValid();
        }
        catch (Exception ex)
        {
            StatusBar.SetMessage(ex.Message, isError: true);
        }
    }

    private void EscapeText()
    {
        ExitOutlineMode();
        var text = _editor.Text;
        if (text.Length == 0)
            return;
        Replace(System.Text.Json.JsonSerializer.Serialize(text));
        StatusBar.SetMessage(Loc.Get("Formatter.Escaped"), isError: false);
    }

    private void UnescapeText()
    {
        ExitOutlineMode();
        var text = _editor.Text;
        if (text.Length == 0)
            return;
        Replace(InputSanitizer.Clean(text));
        RefreshDetection(autoValidate: true);
    }

    private void ConvertTo(IDataFormat target)
    {
        ExitOutlineMode();
        var text = _editor.Text;
        if (text.Length == 0)
            return;

        var source = FormatRegistry.DetectBest(text);
        try
        {
            var node = source.ToCanonical(text);
            var rewritten = target.FromCanonical(node, Options);
            var pretty = target.Format(rewritten, Options);
            if (!pretty.Success)
            {
                Replace(rewritten);
                SetActiveFormat(target);
                StatusBar.SetMessage(pretty.Diagnostics[0].Message, isError: true);
                return;
            }
            Replace(pretty.Text);
            SetActiveFormat(target);
            StatusBar.SetValid();
        }
        catch (Exception ex)
        {
            StatusBar.SetMessage(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Saves the editor's text straight to a file the user picks — the suggested extension
    /// follows the detected (or last-applied) format, so a JSON document doesn't default to
    /// <c>.json</c> while sitting on an XML document. Returns false without touching disk when
    /// there's nothing to save or the user cancels the picker, so the close-without-saving prompt
    /// (<c>ToolWindowBase.OnClosing</c>) knows not to discard the window's unsaved content anyway.
    /// </summary>
    private async Task<bool> SaveToFileAsync()
    {
        ExitOutlineMode();
        if (_editor.Text.Length == 0)
            return false;

        try
        {
            var format = _activeFormat ?? FormatRegistry.DetectBest(_editor.Text);
            string extension = format.Id switch
            {
                "xml" => ".xml",
                _ => ".json",
            };

            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, HostHwnd);
            picker.SuggestedFileName = "data" + extension;
            picker.FileTypeChoices.Add(format.Id.ToUpperInvariant(), new List<string> { extension });
            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return false;

            File.WriteAllText(file.Path, _editor.Text);
            _isDirty = false;
            StatusBar.SetMessage(Loc.Format("Tool.SavedTo", file.Name), isError: false);
            return true;
        }
        catch (Exception ex)
        {
            Diag.Log("FormatterPage.SaveToFileAsync failed: " + ex);
            StatusBar.SetMessage(Loc.Get("Tool.SaveFailed"), isError: true);
            return false;
        }
    }

    private void CopyOutput()
    {
        ExitOutlineMode();
        if (_editor.Text.Length == 0)
            return;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(_editor.Text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private void Clear()
    {
        ExitOutlineMode();
        Replace("");
        _isDirty = false;
        StatusBar.SetUntouched();
    }

    // ---- Search --------------------------------------------------------------------------

    /// <summary>
    /// The find widget: a query box, a "N of M" count, previous/next, and a close button, floated
    /// over the top-right corner of the editor pane (see the constructor's <c>editorArea</c> Grid)
    /// rather than seated in the layout — it has to sit on top of the text it searches without
    /// stealing any of that text's own width.
    /// </summary>
    private Border BuildSearchPanel()
    {
        _searchBox.PlaceholderText = Loc.Get("Formatter.SearchPlaceholder");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_searchBox, Loc.Get("Formatter.Search"));
        _searchBox.TextChanged += (_, _) => RefreshSearchMatches();
        _searchBox.KeyDown += SearchBox_KeyDown;

        var prev = SearchIconButton(SearchPrevGlyph, Loc.Get("Formatter.SearchPrevious"));
        prev.Click += (_, _) => FindPrevious();

        var next = SearchIconButton(SearchNextGlyph, Loc.Get("Formatter.SearchNext"));
        next.Click += (_, _) => FindNext();

        var close = SearchIconButton(SearchCloseGlyph, Loc.Get("Formatter.SearchClose"));
        close.Click += (_, _) => CloseSearchAndFocusEditor();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Padding = new Thickness(6, 4, 4, 4),
            Children = { _searchBox, _searchCountLabel, prev, next, close },
        };

        return new Border
        {
            Child = row,
            Background = ThemeBrush("ShellCardBackgroundBrush"),
            BorderBrush = ThemeBrush("ShellCardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
            Visibility = Visibility.Collapsed,
        };
    }

    private Button SearchIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        ApplyHoverIconColor(button, HoverAccent.Accent);
        return button;
    }

    /// <summary>Opens the panel (a no-op if it's already open beyond re-focusing the query box)
    /// and selects whatever text is already in it, so pressing the search shortcut again is a
    /// fast way to retype the query rather than a way to close the panel — <see cref="CloseSearch"/>
    /// and its Escape/close-button callers are the only way back out, which keeps one predictable
    /// meaning for each control instead of overloading the search button as a toggle.</summary>
    private void OpenSearch()
    {
        ExitOutlineMode();
        _searchPanel.Visibility = Visibility.Visible;
        _searchBox.Focus(FocusState.Programmatic);
        _searchBox.SelectAll();
        RefreshSearchMatches();
    }

    /// <summary>Closes the panel and drops the stale match list. Safe to call whether or not the
    /// panel is actually open — the editor's own TextChanged handler calls this unconditionally
    /// whenever the document goes empty.</summary>
    private void CloseSearch()
    {
        if (_searchPanel.Visibility != Visibility.Visible)
            return;
        _searchPanel.Visibility = Visibility.Collapsed;
        _searchMatches.Clear();
        _searchCurrentMatch = -1;
        UpdateSearchLabel();
    }

    private void CloseSearchAndFocusEditor()
    {
        CloseSearch();
        _editor.Focus();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            CloseSearchAndFocusEditor();
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Enter)
            return;

        bool shift = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (shift)
            FindPrevious();
        else
            FindNext();
        e.Handled = true;
    }

    /// <summary>
    /// Rescans <see cref="CodeEditor.Text"/> for every non-overlapping, case-insensitive
    /// occurrence of the search box's own text and lands on the first one. Called on every
    /// keystroke in the search box and, while the panel is open, on every change to the document
    /// itself (Format, Minify, Convert, or plain typing) — offsets computed against one version
    /// of the text are meaningless against the next, so this always starts over from scratch
    /// rather than trying to patch the previous match list.
    /// </summary>
    private void RefreshSearchMatches()
    {
        _searchMatches.Clear();
        _searchCurrentMatch = -1;

        string query = _searchBox.Text;
        if (query.Length > 0)
        {
            string text = _editor.Text;
            int index = 0;
            while (index <= text.Length - query.Length)
            {
                int found = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    break;
                _searchMatches.Add(found);
                index = found + query.Length;
            }
        }

        if (_searchMatches.Count > 0)
            SelectMatch(0, moveFocus: false);
        else
            UpdateSearchLabel();
    }

    private void FindNext()
    {
        if (_searchMatches.Count > 0)
            SelectMatch(_searchCurrentMatch + 1, moveFocus: true);
    }

    private void FindPrevious()
    {
        if (_searchMatches.Count > 0)
            SelectMatch(_searchCurrentMatch - 1, moveFocus: true);
    }

    /// <summary>
    /// Selects match <paramref name="index"/> (wrapping in either direction) in the editor's own
    /// document — offsets from <see cref="CodeEditor.Text"/> apply directly to
    /// <c>TextBox.Document</c>, the same one-character-for-one-character correspondence the
    /// syntax highlighter already relies on (see <see cref="CodeEditor.Text"/>'s doc comment).
    /// <para>
    /// <paramref name="moveFocus"/> is false for the live-as-you-type call from
    /// <see cref="RefreshSearchMatches"/> and true for an explicit Enter/Next/Previous — focusing
    /// the RichEditBox is what scrolls the match into view (there's no separate "scroll into
    /// view" call on this control's document model), but doing that on <em>every keystroke</em>
    /// pulled focus out of the search box after the very first character, so the box could never
    /// actually be typed into. Only a deliberate "go to this match" action gets to move focus;
    /// typing always keeps it right where the user left it.
    /// </para>
    /// </summary>
    private void SelectMatch(int index, bool moveFocus)
    {
        _searchCurrentMatch = ((index % _searchMatches.Count) + _searchMatches.Count) % _searchMatches.Count;
        int start = _searchMatches[_searchCurrentMatch];
        int length = _searchBox.Text.Length;
        _editor.TextBox.Document.Selection.SetRange(start, start + length);
        if (moveFocus)
            _editor.TextBox.Focus(FocusState.Programmatic);
        UpdateSearchLabel();
    }

    private void UpdateSearchLabel()
    {
        _searchCountLabel.Text = _searchMatches.Count > 0
            ? Loc.Format("Formatter.SearchMatchCount", _searchCurrentMatch + 1, _searchMatches.Count)
            : _searchBox.Text.Length > 0 ? Loc.Get("Formatter.SearchNoMatches") : "";
    }

    // ---- Outline (collapse/expand) --------------------------------------------------------

    private void ToggleOutline()
    {
        if (_outlineActive)
            ExitOutlineMode();
        else
            EnterOutlineMode();
    }

    /// <summary>
    /// Freezes the current document as <see cref="_trueText"/> and finds every foldable JSON/XML
    /// node in it. Compact/unformatted input has nothing to fold on its own — this pretty-prints
    /// it first in that case, so Outline always has something to show for genuinely nested
    /// content regardless of whether the user ran Format first.
    /// <para>
    /// <b>Why editing is never blocked.</b> The RichEditBox this editor wraps has no notion of
    /// hidden/folded text in the API WinUI exposes — a collapsed node is represented by actually
    /// deleting its inner text from the document and remembering the removed range against
    /// <see cref="_trueText"/> (see <see cref="RenderOutline"/>), which only stays correct for as
    /// long as nothing edits the document out from under it. Rather than lock the editor for
    /// that whole time, <see cref="_editor"/>'s RichEditBox has a <c>GotFocus</c> handler (wired
    /// in the constructor) that calls <see cref="ExitOutlineMode"/> the instant the user clicks
    /// or tabs into it — before any keystroke can land — so the full text is always back in place
    /// before editing actually starts. The trade-off is that a click made while folded doesn't
    /// keep its exact caret position once the full text reappears; the alternative (blocking
    /// input entirely) was rejected as not actually being "still editable."
    /// </para>
    /// </summary>
    private void EnterOutlineMode()
    {
        if (_outlineActive || _editor.Text.Length == 0)
            return;

        CloseSearch();
        var format = _activeFormat ?? FormatRegistry.DetectBest(_editor.Text);
        string text = _editor.Text;
        var regions = FoldFinder.Find(text, format.Id);

        if (regions.Count == 0)
        {
            var formatted = format.Format(text, Options);
            if (formatted.Success)
            {
                var formattedRegions = FoldFinder.Find(formatted.Text, format.Id);
                if (formattedRegions.Count > 0)
                {
                    text = formatted.Text;
                    regions = formattedRegions;
                }
            }
        }

        if (regions.Count == 0)
        {
            StatusBar.SetMessage(Loc.Get("Formatter.OutlineNothingToFold"), isError: false);
            return;
        }

        _trueText = text;
        _foldRegions = regions;
        _collapsedStarts.Clear();
        _outlineActive = true;
        SetActiveFormat(format);
        RenderOutline();
        StatusBar.SetMessage(Loc.Get("Formatter.OutlineActive"), isError: false);
    }

    /// <summary>
    /// Restores <see cref="_trueText"/> verbatim and drops every trace of the outline session.
    /// Safe to call whether or not one is active — every command that reads or replaces the
    /// document's real content calls this first, unconditionally, so the true text is always
    /// what they see regardless of fold state, and so does the RichEditBox's own
    /// <c>GotFocus</c> the moment the user starts interacting with it again (see
    /// <see cref="EnterOutlineMode"/>'s doc comment).
    /// </summary>
    private void ExitOutlineMode()
    {
        if (!_outlineActive)
            return;

        string original = _trueText!;
        _outlineActive = false;
        _editor.GutterMarkers = null;
        _foldRegions = new List<FoldRegion>();
        _collapsedStarts.Clear();
        _trueText = null;

        SetEditorDisplayText(original);
        StatusBar.SetCounts(original);
    }

    private void ToggleFold(FoldRegion region)
    {
        if (!_collapsedStarts.Remove(region.Start))
            _collapsedStarts.Add(region.Start);
        RenderOutline();
    }

    /// <summary>
    /// Rebuilds the editor's displayed text and gutter markers from <see cref="_trueText"/> and
    /// the current <see cref="_collapsedStarts"/> — the nesting/offset arithmetic itself lives in
    /// <see cref="FoldRenderer.Render"/>, tested independently of this page; this just turns each
    /// plain <see cref="FoldMarker"/> it returns into a clickable <see cref="CodeEditor.GutterMarker"/>.
    /// </summary>
    private void RenderOutline()
    {
        var (displayText, foldMarkers) = FoldRenderer.Render(_trueText!, _foldRegions, _collapsedStarts);

        var markers = new Dictionary<int, CodeEditor.GutterMarker>();
        foreach (var marker in foldMarkers)
        {
            var region = marker.Region; // local copy for the closure below
            markers[marker.Line] = new CodeEditor.GutterMarker(
                marker.Collapsed ? FoldCollapsedSymbol : FoldExpandedSymbol,
                () => ToggleFold(region));
        }

        _editor.GutterMarkers = markers;
        SetEditorDisplayText(displayText);
    }

    /// <summary>Assigns the editor's text while suppressing the dirty flag, status counts, and
    /// format re-detection that a real edit would trigger — folding isn't new content, and while
    /// active it's frozen (see <see cref="EnterOutlineMode"/>'s doc comment), so none of that
    /// should move. Search-panel and command-visibility upkeep still run (see the constructor's
    /// TextChanged handler): both are correct to re-check against whatever's on screen right now.</summary>
    private void SetEditorDisplayText(string displayText)
    {
        _replacing = true;
        try
        {
            _editor.Text = displayText;
        }
        finally
        {
            _replacing = false;
        }
    }

    private Border BuildAdvancedSidebar()
    {
        var stack = new StackPanel
        {
            Spacing = 10,
            Padding = new Thickness(12),
            Width = AdvancedSidebarWidth,
        };

        stack.Children.Add(new TextBlock
        {
            Text = Loc.Get("Tool.Advanced"),
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 4),
        });

        stack.Children.Add(SidebarButton(Loc.Get("Formatter.Outline"), OutlineGlyph, ToggleOutline));
        stack.Children.Add(SidebarButton(Loc.Get("Tool.Minify"), "\uE37F", Minify));
        stack.Children.Add(SidebarButton(Loc.Get("Formatter.SortKeys"), "\uE8CB", SortKeys));
        stack.Children.Add(SidebarButton(Loc.Get("Formatter.Escape"), "\uE8AB", EscapeText));
        stack.Children.Add(SidebarButton(Loc.Get("Formatter.Unescape"), "\uE8AC", UnescapeText));

        // Stacked label-above-box rather than side-by-side: at the narrower sidebar width, "Indent
        // width" next to the spin box left no room for either to breathe without wrapping.
        var indentRow = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        indentRow.Children.Add(new TextBlock { Text = Loc.Get("Formatter.Indent") });
        indentRow.Children.Add(_indentBox);
        stack.Children.Add(new Border { Child = indentRow, Margin = new Thickness(0, 8, 0, 0) });

        stack.Children.Add(new TextBlock
        {
            Text = Loc.Get("Formatter.Convert"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 8, 0, 0),
        });
        stack.Children.Add(SidebarButton(Loc.Get("Formatter.ToJson"), "\uE943", () => ConvertTo(FormatRegistry.Json)));
        stack.Children.Add(SidebarButton(Loc.Get("Formatter.ToXml"), "</>", () => ConvertTo(FormatRegistry.Xml)));

        return new Border
        {
            Child = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            Background = ThemeBrush("ShellPillBrush"),
            BorderBrush = ThemeBrush("ShellCardBorderBrush"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
    }

    private Button SidebarButton(string label, string glyph, Action action)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (GlyphFonts.IsTextGlyph(glyph))
            icon.FontFamily = new FontFamily("Segoe UI");
        else if (Application.Current.Resources.TryGetValue("SymbolThemeFontFamily", out var font) && font is FontFamily family)
            icon.FontFamily = family;

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 8, 10, 8),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    icon,
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        // The button's content is a panel, not a string, so it has no accessible name of its own
        // to fall back on — and what a peer would scrape from that panel starts with the icon,
        // whose glyph is a private-use codepoint Narrator reads as nothing useful. Name the
        // button from its label and take the icon out of the accessibility tree, per §7.2: the
        // glyph repeats what the label already says.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(
            icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        button.Click += (_, _) => action();
        ApplyHoverIconColor(button, HoverAccent.Accent);
        return button;
    }

}
