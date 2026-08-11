using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using SwiftX.Services.Formats;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace SwiftX.ToolPages;

/// <summary>
/// Backs the JSON, Data Formatter and XML catalog entries — catalog entries share one page,
/// differing by which <see cref="IDataFormat"/> they're constructed with. Basic chrome stays
/// lean (Format / Validate / Copy / Clear); Advanced opens a sidebar for minify, sort, escape,
/// convert and indent. Invalid input never clears the user's text.
/// </summary>
public sealed class FormatterPage : EditorToolPage
{
    private const int DefaultIndentWidth = 2;
    private const double IndentBoxWidth = 60;
    private const double AdvancedSidebarWidth = 260;

    private readonly IDataFormat? _fixedFormat;
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
    private readonly TextBlock _detectedLabel = new()
    {
        Opacity = 0.75,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0),
    };

    private IReadOnlyList<ToolCommand>? _commands;
    private IDataFormat? _activeFormat;
    private bool _isDirty;
    private bool _replacing;
    private readonly ToolKind _kind;
    private readonly DispatcherQueueTimer _detectTimer;

    public FormatterPage(IDataFormat format)
    {
        _kind = ResolveKind(format);
        _fixedFormat = ReferenceEquals(format, FormatRegistry.Auto) ? null : format;

        _editor.PlaceholderText = Loc.Get("Formatter.InputPlaceholder");
        _editor.AccessibleName = Loc.Get("Common.Input");
        if (_fixedFormat is not null)
            _editor.Tokenizer = _fixedFormat.Tokenizer;

        _detectTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
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

        ToolTipService.SetToolTip(_indentBox, Loc.Get("Formatter.Indent"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_indentBox, Loc.Get("Formatter.Indent"));
        _indentBox.EnableWheelStep(1);

        _advancedToggle = new ToggleButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE713", FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = Loc.Get("Tool.Advanced"), VerticalAlignment = VerticalAlignment.Center },
                },
            },
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 4, 10, 4),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_advancedToggle, ToolCommand.Ids.Advanced);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_advancedToggle, Loc.Get("Tool.Advanced"));

        _advancedPanel = BuildAdvancedSidebar();
        _advancedToggle.Checked += (_, _) => _advancedPanel.Visibility = Visibility.Visible;
        _advancedToggle.Unchecked += (_, _) => _advancedPanel.Visibility = Visibility.Collapsed;

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var editorPane = Pane(_editor);
        Grid.SetColumn(editorPane, 0);
        Grid.SetColumn(_advancedPanel, 1);
        body.Children.Add(editorPane);
        body.Children.Add(_advancedPanel);

        var options = OptionsBar();
        options.Children.Add(_advancedToggle);
        options.Children.Add(_detectedLabel);
        SetOptions(options);

        SetBody(body);
        StatusBar.SetUntouched();
        InitializeChrome();
        RefreshDetection(autoValidate: false);
    }

    public override ToolKind Kind => _kind;
    public override bool IsDirty => _isDirty;

    /// <summary>Basic bar only — Format, Validate, Copy, Clear. Advanced lives in the sidebar.</summary>
    public override IReadOnlyList<ToolCommand> Commands => _commands ??=
    [
        new ToolCommand(Loc.Get("Tool.Format"), "\uE945", Format, VirtualKey.Enter, VirtualKeyModifiers.Control, id: ToolCommand.Ids.Format),
        ToolCommand.Validate(ValidateAndClean),
        ToolCommand.Copy(CopyOutput),
        ToolCommand.Clear(Clear),
    ];

    public override bool AcceptsClipboardText(string text) =>
        text.Length > 0 && (_fixedFormat ?? FormatRegistry.DetectBest(text)).DetectConfidence(text) > 0;

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

        SetActiveFormat(_fixedFormat ?? FormatRegistry.DetectBest(text));
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
        var format = _fixedFormat ?? FormatRegistry.DetectBest(cleaned);
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

        var format = _fixedFormat ?? FormatRegistry.DetectBest(text);
        UpdateDetectedLabel(format);

        if (_fixedFormat is null)
            _editor.Tokenizer = format.Tokenizer;

        if (!autoValidate)
            return;

        var diagnostics = format.Validate(text);
        if (diagnostics.Count == 0)
            StatusBar.SetValid();
        else
            StatusBar.SetError(diagnostics[0].Line, diagnostics[0].Column);
    }

    private void UpdateDetectedLabel(IDataFormat format)
    {
        string name = format.Id.ToUpperInvariant();
        _detectedLabel.Text = _fixedFormat is null
            ? Loc.Format("Formatter.Detected", name)
            : Loc.Format("Formatter.FormatName", name);
    }

    private void Minify()
    {
        var text = _editor.Text;
        if (text.Length == 0)
            return;
        SetActiveFormat(_fixedFormat ?? FormatRegistry.DetectBest(text));
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

        var format = _fixedFormat ?? FormatRegistry.DetectBest(text);
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

        var source = _fixedFormat ?? FormatRegistry.DetectBest(text);
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

        stack.Children.Add(new TextBlock
        {
            Text = Loc.Get("Formatter.Convert"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 8, 0, 0),
        });
        stack.Children.Add(SidebarButton(Loc.Get("Formatter.ToJson"), "\uE943", () => ConvertTo(FormatRegistry.Json)));
        stack.Children.Add(SidebarButton(Loc.Get("Formatter.ToXml"), "</>", () => ConvertTo(FormatRegistry.Xml)));
        stack.Children.Add(SidebarButton(Loc.Get("Formatter.ToCsv"), "\uE8A5", () => ConvertTo(FormatRegistry.Csv)));

        var indentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        indentRow.Children.Add(new TextBlock
        {
            Text = Loc.Get("Formatter.Indent"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        indentRow.Children.Add(_indentBox);
        stack.Children.Add(new Border { Child = indentRow, Margin = new Thickness(0, 8, 0, 0) });

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

    private static ToolKind ResolveKind(IDataFormat format)
    {
        if (ReferenceEquals(format, FormatRegistry.Xml))
            return ToolKind.Xml;
        if (ReferenceEquals(format, FormatRegistry.Auto))
            return ToolKind.Data;
        return ToolKind.Json;
    }
}
