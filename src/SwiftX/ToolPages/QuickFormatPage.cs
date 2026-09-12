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
/// The lightweight sibling of <see cref="FormatterPage"/> ("Data Formatter"): no command bar, no
/// Advanced sidebar, no Format/Validate/Search/Convert. Opening it reads whatever text is already
/// on the clipboard, auto-detects JSON, XML, YAML or CSV via <see cref="FormatRegistry"/>, and
/// formats it with the industry-standard 2-space indent (<see cref="FormatOptions.Default"/>) —
/// an empty clipboard just leaves the editor's placeholder inviting a paste, the same "nothing
/// copied yet" state <see cref="FormatterPage"/> shows. A paste always replaces the document with
/// the clipboard's current text and reformats it, rather than inserting at the caret: this tool
/// shows one payload at a time, so "I copied something else, paste it in" should look exactly like
/// reopening the tool, not like editing a document with two payloads spliced together.
/// <para>
/// Three icon buttons live in the title bar (<see cref="HeaderActions"/>) rather than a command
/// bar — Open Data Formatter, Save, Copy, in that order — which is what lets the command bar
/// collapse away entirely (see <see cref="EditorToolPage.InitializeChrome"/>, the same trick
/// <see cref="SecureValuesPage"/> already uses for its own title-bar-only Save). Open Data
/// Formatter hands off to the full editing tool (<see cref="ToolKind.Data"/>) via
/// <see cref="ToolPage.HostManager"/> for anything beyond a quick glance.
/// </para>
/// </summary>
public sealed class QuickFormatPage : EditorToolPage
{
    // Built from the codepoint, not a literal character in source, so the glyph can't be silently
    // swapped for a different (or invisible) private-use character by a stray copy/paste — see
    // the memory note on this exact failure mode. Same codepoints as SecureValuesPage/
    // CertificatePage's Copy, FormatterPage's Save, and the Data Formatter catalog entry's own
    // glyph, for one consistent icon vocabulary.
    private static readonly string OpenFormatterGlyph = char.ConvertFromUtf32(0xE943);
    private static readonly string SaveGlyph = char.ConvertFromUtf32(0xE74E);
    private static readonly string CopyGlyph = char.ConvertFromUtf32(0xE8C8);

    private readonly CodeEditor _editor = new();
    private readonly FrameworkElement _headerActions;
    private readonly TextBlock _detectedLabel = new()
    {
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Opacity = 0.75,
        Margin = new Thickness(12, 8, 12, 6),
    };

    private IDataFormat? _activeFormat;

    public QuickFormatPage()
    {
        _editor.PlaceholderText = Loc.Get("QuickFormat.InputPlaceholder");
        _editor.AccessibleName = Loc.Get("Common.Input");

        // A paste is a deliberate "here's a document" moment: reload straight from the clipboard
        // (the same call the initial open uses) rather than trusting whatever RichEditBox merged
        // at the caret, so the result is always exactly the pasted payload, formatted — see the
        // type doc comment.
        _editor.Pasted += (_, _) => _ = LoadFromClipboardAsync();
        _editor.TextChanged += (_, _) =>
        {
            if (_editor.Text.Length == 0)
                _detectedLabel.Text = "";
        };

        var openFormatterButton = IconButton(OpenFormatterGlyph, Loc.Get("QuickFormat.OpenDataFormatter"), HoverAccent.Accent);
        openFormatterButton.Click += (_, _) => HostManager?.OpenTool(ToolKind.Data);

        var saveButton = IconButton(SaveGlyph, Loc.Get("Tool.Save"), HoverAccent.Accent);
        saveButton.Click += (_, _) => _ = SaveToFileAsync();

        var copyButton = IconButton(CopyGlyph, Loc.Get("Tool.CopyOutput"), HoverAccent.Copy);
        copyButton.Click += (_, _) => CopyOutput();

        _headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { openFormatterButton, saveButton, copyButton },
        };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_detectedLabel, 0);
        var pane = Pane(_editor);
        Grid.SetRow(pane, 1);
        body.Children.Add(_detectedLabel);
        body.Children.Add(pane);

        SetBody(body);
        InitializeChrome();

        _ = LoadFromClipboardAsync();
    }

    public override ToolKind Kind => ToolKind.QuickFormat;

    /// <summary>
    /// Always false, even though the editor is now writable: this tool shows one glanced-at
    /// payload rather than a document the user is composing, its source content is still sitting
    /// wherever it was originally copied from, and forcing a save/discard prompt on every close
    /// would be exactly the friction a "quick" tool shouldn't have.
    /// </summary>
    public override bool IsDirty => false;

    public override bool CanSave => true;
    public override FrameworkElement? HeaderActions => _headerActions;

    /// <summary>No command-bar commands — Open Data Formatter, Save and Copy live in the title bar
    /// instead (see <see cref="HeaderActions"/>), which also lets the (now empty) command bar
    /// collapse.</summary>
    public override IReadOnlyList<ToolCommand> Commands => [];

    public override Task<bool> SaveAsync() => SaveToFileAsync();

    public override bool AcceptsClipboardText(string text) => text.Length > 0;

    public override void PasteClipboardText(string text) => ApplyText(text);

    public override async Task LoadFileAsync(string path)
    {
        var read = await InputLimits.ReadTextAsync(path);
        if (!read.Ok)
        {
            _detectedLabel.Text = read.Error;
            return;
        }
        ApplyText(read.Text);
    }

    /// <summary>
    /// Reads the clipboard and, if there is text on it, formats it into the editor — used both for
    /// the auto-run on open and for every paste (see the type doc comment on why a paste re-reads
    /// the clipboard rather than trusting the caret-inserted text). Leaves the editor exactly as it
    /// was for every non-fatal case (nothing on the clipboard, clipboard access denied): the
    /// placeholder already invites a paste, so there is nothing to show for those beyond that.
    /// </summary>
    private async Task LoadFromClipboardAsync()
    {
        string text;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
                return;
            text = await content.GetTextAsync();
        }
        catch (Exception ex)
        {
            Diag.Log("QuickFormatPage.LoadFromClipboardAsync failed: " + ex);
            return;
        }

        if (PageClosing.IsCancellationRequested) return;
        ApplyText(text);
    }

    private void ApplyText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var cleaned = InputSanitizer.Clean(text);
        if (cleaned.Length == 0)
            return;

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
        _editor.Text = text;
        _editor.Tokenizer = format?.Tokenizer ?? PlainTokenizer.Instance;
        _detectedLabel.Text = format is null
            ? Loc.Get("QuickFormat.PlainText")
            : Loc.Format("Formatter.FormatName", format.Id.ToUpperInvariant());
    }

    private void CopyOutput()
    {
        if (_editor.Text.Length == 0)
            return;
        var package = new DataPackage();
        package.SetText(_editor.Text);
        Clipboard.SetContent(package);
    }

    private async Task<bool> SaveToFileAsync()
    {
        if (_editor.Text.Length == 0)
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

            File.WriteAllText(file.Path, _editor.Text);
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
