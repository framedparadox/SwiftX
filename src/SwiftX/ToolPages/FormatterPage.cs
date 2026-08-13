using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using SwiftX.Services.Formats;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace SwiftX.ToolPages;

/// <summary>
/// Backs the one Data Formatter catalog entry: auto-detects JSON, XML, or CSV and formats it.
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

    private IReadOnlyList<ToolCommand>? _commands;
    private IDataFormat? _activeFormat;
    private bool _isDirty;
    private bool _replacing;
    private readonly DispatcherQueueTimer _detectTimer;

    public FormatterPage()
    {
        _editor.PlaceholderText = Loc.Get("Formatter.InputPlaceholder");
        _editor.AccessibleName = Loc.Get("Common.Input");

        _detectTimer = DispatcherQueue.CreateTimer();
        _detectTimer.Interval = TimeSpan.FromMilliseconds(280);
        _detectTimer.IsRepeating = false;
        _detectTimer.Tick += (_, _) => RefreshDetection(autoValidate: true);

        _editor.TextChanged += (_, _) =>
        {
            if (_replacing)
                return;
            _isDirty = _editor.Text.Length > 0;
            StatusBar.SetCounts(_editor.Text);
            _detectTimer.Stop();
            _detectTimer.Start();
        };

        // A paste is a deliberate "here's a document" moment, unlike the keystrokes TextChanged
        // debounces above — so it gets the full clean+format+detect pass immediately, the same
        // one the ring's "paste clipboard" chip already triggers via PasteClipboardText.
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
        Grid.SetColumn(editorPane, 0);
        Grid.SetColumn(_advancedPanel, 1);
        body.Children.Add(editorPane);
        body.Children.Add(_advancedPanel);

        SetBody(body);
        SetFooter(_detectedLabel);
        StatusBar.SetUntouched();
        InitializeChrome();
        RefreshDetection(autoValidate: false);
    }

    public override ToolKind Kind => ToolKind.Data;
    public override bool IsDirty => _isDirty;
    public override bool CanSave => true;

    /// <summary>The Advanced toggle and Save live in the title bar, next to minimize/maximize,
    /// rather than the command bar — important enough to stay reachable without opening it.</summary>
    public override FrameworkElement? HeaderActions => _headerActions;

    public override Task<bool> SaveAsync() => SaveToFileAsync();

    /// <summary>Basic bar only — Format, Validate, Copy, Clear. Advanced lives in the sidebar.</summary>
    public override IReadOnlyList<ToolCommand> Commands => _commands ??=
    [
        new ToolCommand(Loc.Get("Tool.Format"), "\uE8E3", Format, VirtualKey.Enter, VirtualKeyModifiers.Control, id: ToolCommand.Ids.Format),
        ToolCommand.Validate(ValidateAndClean),
        ToolCommand.Copy(CopyOutput),
        ToolCommand.Clear(Clear),
    ];

    public override bool AcceptsClipboardText(string text) =>
        text.Length > 0 && FormatRegistry.DetectBest(text).DetectConfidence(text) > 0;

    public override void PasteClipboardText(string text)
    {
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
        var text = _editor.Text;
        if (text.Length == 0)
            return;
        Replace(System.Text.Json.JsonSerializer.Serialize(text));
        StatusBar.SetMessage(Loc.Get("Formatter.Escaped"), isError: false);
    }

    private void UnescapeText()
    {
        var text = _editor.Text;
        if (text.Length == 0)
            return;
        Replace(InputSanitizer.Clean(text));
        RefreshDetection(autoValidate: true);
    }

    private void ConvertTo(IDataFormat target)
    {
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
        if (_editor.Text.Length == 0)
            return false;

        try
        {
            var format = _activeFormat ?? FormatRegistry.DetectBest(_editor.Text);
            string extension = format.Id switch
            {
                "xml" => ".xml",
                "csv" => ".csv",
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
        if (_editor.Text.Length == 0)
            return;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(_editor.Text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private void Clear()
    {
        Replace("");
        _isDirty = false;
        StatusBar.SetUntouched();
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
        stack.Children.Add(SidebarButton(Loc.Get("Formatter.ToCsv"), "\uE8A5", () => ConvertTo(FormatRegistry.Csv)));

        return new Border
        {
            Child = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            Background = ThemeResourceBrush("CardBackgroundFillColorSecondaryBrush"),
            BorderBrush = ThemeResourceBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
    }

    private static Button SidebarButton(string label, string glyph, Action action)
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
        button.Click += (_, _) => action();
        return button;
    }

    private static Brush? ThemeResourceBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value)
            ? value as Brush
            : null;
}
