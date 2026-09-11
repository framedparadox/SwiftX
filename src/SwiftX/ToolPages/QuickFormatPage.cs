using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using SwiftX.Services.Formats;
using SwiftX.Services.Syntax;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SwiftX.ToolPages;

/// <summary>
/// The lightweight sibling of <see cref="FormatterPage"/> ("Data Formatter"): no editing surface,
/// no command bar, no Advanced sidebar. Opening it reads whatever text is already on the
/// clipboard, auto-detects JSON, XML, YAML or CSV via <see cref="FormatRegistry"/>, formats it
/// with the industry-standard 2-space indent (<see cref="FormatOptions.Default"/>), and shows the
/// result read-only. Copy and Save are the only two actions — icon buttons living in the title bar
/// (<see cref="HeaderActions"/>) rather than a command bar; an empty <see cref="Commands"/> list is
/// what lets that bar collapse away entirely (see <see cref="EditorToolPage.InitializeChrome"/>,
/// the same trick <see cref="SecureValuesPage"/> already uses for its own title-bar-only Save).
/// <para>
/// This exists alongside Data Formatter rather than as a mode of it: Data Formatter is the editing
/// workspace (Format/Validate/Search/Minify/Sort/Convert, a resizable 900x640 window) for working
/// on a document; this is the "glance at what I just copied" tool — auto-run, no editing, a small
/// fixed-purpose window (see its <see cref="ToolDefinition.WindowSize"/> in
/// <see cref="ToolCatalog"/>) that a user opens, reads, copies or saves, and closes.
/// </para>
/// </summary>
public sealed class QuickFormatPage : EditorToolPage
{
    // Built from the codepoint, not a literal character in source, so the glyph can't be silently
    // swapped for a different (or invisible) private-use character by a stray copy/paste — see
    // the memory note on this exact failure mode. Same codepoints as SecureValuesPage/
    // CertificatePage's Copy and FormatterPage's Save, for one consistent icon vocabulary.
    private static readonly string CopyGlyph = char.ConvertFromUtf32(0xE8C8);
    private static readonly string SaveGlyph = char.ConvertFromUtf32(0xE74E);

    private readonly CodeView _view = new();
    private readonly FrameworkElement _headerActions;
    private readonly TextBlock _statusLabel = new()
    {
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Opacity = 0.75,
        Margin = new Thickness(12, 8, 12, 6),
    };

    private IDataFormat? _activeFormat;

    public QuickFormatPage()
    {
        _view.AccessibleName = Loc.Get("QuickFormat.Output");

        var copyButton = IconButton(CopyGlyph, Loc.Get("Tool.CopyOutput"), HoverAccent.Copy);
        copyButton.Click += (_, _) => CopyOutput();

        var saveButton = IconButton(SaveGlyph, Loc.Get("Tool.Save"), HoverAccent.Accent);
        saveButton.Click += (_, _) => _ = SaveToFileAsync();

        _headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { copyButton, saveButton },
        };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_statusLabel, 0);
        var pane = Pane(_view);
        Grid.SetRow(pane, 1);
        body.Children.Add(_statusLabel);
        body.Children.Add(pane);

        SetBody(body);
        InitializeChrome();

        ShowStatus(Loc.Get("QuickFormat.Loading"));
        _ = LoadFromClipboardAsync();
    }

    public override ToolKind Kind => ToolKind.QuickFormat;
    public override bool IsDirty => false;
    public override bool CanSave => true;
    public override FrameworkElement? HeaderActions => _headerActions;

    /// <summary>No command-bar commands — Copy and Save live in the title bar instead (see
    /// <see cref="HeaderActions"/>), which also lets the (now empty) command bar collapse.</summary>
    public override IReadOnlyList<ToolCommand> Commands => [];

    public override Task<bool> SaveAsync() => SaveToFileAsync();

    public override bool AcceptsClipboardText(string text) => text.Length > 0;

    public override void PasteClipboardText(string text) => ApplyText(text);

    public override async Task LoadFileAsync(string path)
    {
        var read = await InputLimits.ReadTextAsync(path);
        if (!read.Ok)
        {
            ShowStatus(read.Error);
            return;
        }
        ApplyText(read.Text);
    }

    /// <summary>
    /// The auto-run entry point: reads the clipboard the instant the window opens (rather than
    /// waiting for a paste), so "quickly format the data I already copied" needs no action beyond
    /// opening the tool. Falls back to a friendly status message rather than an error state for
    /// every non-fatal case (no text on the clipboard, clipboard access denied) — there is nothing
    /// wrong here for the user to fix, just nothing to show yet.
    /// </summary>
    private async Task LoadFromClipboardAsync()
    {
        string text;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                if (!PageClosing.IsCancellationRequested) ShowStatus(Loc.Get("QuickFormat.ClipboardEmpty"));
                return;
            }
            text = await content.GetTextAsync();
        }
        catch (Exception ex)
        {
            Diag.Log("QuickFormatPage.LoadFromClipboardAsync failed: " + ex);
            if (!PageClosing.IsCancellationRequested) ShowStatus(Loc.Get("QuickFormat.ClipboardUnavailable"));
            return;
        }

        if (PageClosing.IsCancellationRequested) return;
        ApplyText(text);
    }

    private void ApplyText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            ShowStatus(Loc.Get("QuickFormat.ClipboardEmpty"));
            return;
        }

        var cleaned = InputSanitizer.Clean(text);
        if (cleaned.Length == 0)
        {
            ShowStatus(Loc.Get("QuickFormat.ClipboardEmpty"));
            return;
        }

        var format = FormatRegistry.DetectBest(cleaned);
        var result = format.Format(cleaned, FormatOptions.Default);
        if (result.Success)
            SetContent(result.Text, format);
        else
            SetContent(cleaned, null);
    }

    private void SetContent(string text, IDataFormat? format)
    {
        _activeFormat = format;
        var tokenizer = format?.Tokenizer ?? PlainTokenizer.Instance;
        _view.SetContent(text, tokenizer.Tokenize(text));
        _statusLabel.Text = format is null
            ? Loc.Get("QuickFormat.PlainText")
            : Loc.Format("Formatter.FormatName", format.Id.ToUpperInvariant());
    }

    private void ShowStatus(string message)
    {
        _activeFormat = null;
        _view.Clear();
        _statusLabel.Text = message;
    }

    private void CopyOutput()
    {
        if (_view.Text.Length == 0)
            return;
        var package = new DataPackage();
        package.SetText(_view.Text);
        Clipboard.SetContent(package);
    }

    private async Task<bool> SaveToFileAsync()
    {
        if (_view.Text.Length == 0)
            return false;

        try
        {
            string extension = _activeFormat?.Id switch
            {
                "json" => ".json",
                "xml" => ".xml",
                "yaml" => ".yaml",
                "csv" => ".csv",
                _ => ".txt",
            };

            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, HostHwnd);
            picker.SuggestedFileName = "data" + extension;
            picker.FileTypeChoices.Add((_activeFormat?.Id ?? "text").ToUpperInvariant(), new List<string> { extension });
            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return false;

            File.WriteAllText(file.Path, _view.Text);
            return true;
        }
        catch (Exception ex)
        {
            Diag.Log("QuickFormatPage.SaveToFileAsync failed: " + ex);
            return false;
        }
    }

    private static Button IconButton(string glyph, string tooltip, HoverAccent accent)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        ApplyHoverIconColor(button, accent);
        return button;
    }
}
