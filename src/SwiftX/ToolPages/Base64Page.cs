using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwiftX.ToolPages;

/// <summary>
/// Basic Base64 encode/decode: one editor pane, an Encode and a Decode button, and a
/// URL-safe toggle for the output alphabet. Decode is deliberately forgiving on input (see
/// <see cref="Base64Codec.Decode"/>) but never clears the user's text on failure — same
/// contract as <see cref="FormatterPage"/>.
/// </summary>
public sealed class Base64Page : EditorToolPage
{
    // Built from the codepoint rather than a source literal, so the glyph can't be
    // silently swapped for a different (or invisible) private-use character by a stray
    // copy/paste -- that failure mode leaves nothing visibly wrong to catch by eye.
    private static readonly string EncodeGlyph = char.ConvertFromUtf32(0xE8AB);
    private static readonly string DecodeGlyph = char.ConvertFromUtf32(0xE8AC);

    private readonly CodeEditor _editor = new();

    // The footer's other text (ToolStatusBar's counts/chip, the privacy tooltip) all render at
    // the Fluent caption size; a bare string here would fall back to the CheckBox's default body
    // size instead and read as a mismatched control dropped into the row.
    private readonly CheckBox _urlSafe = new()
    {
        Content = new TextBlock
        {
            Text = Loc.Get("Base64.UrlSafe"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        },
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _detectedLabel = new()
    {
        Opacity = 0.75,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0),
    };

    private IReadOnlyList<ToolCommand>? _commands;
    private bool _isDirty;
    private bool _replacing;
    private readonly DispatcherQueueTimer _detectTimer;

    public Base64Page(OrbitManager manager) : base(manager)
    {
        _editor.PlaceholderText = Loc.Get("Base64.InputPlaceholder");
        _editor.AccessibleName = Loc.Get("Common.Input");

        _detectTimer = DispatcherQueue.CreateTimer();
        _detectTimer.Interval = TimeSpan.FromMilliseconds(280);
        _detectTimer.IsRepeating = false;
        _detectTimer.Tick += (_, _) => RefreshDetection();

        // Same closed-window race as CodeEditor.Highlight (see FormatterPage's identical guard):
        // typing then closing the window within the 280ms debounce can leave this tick queued
        // against a RichEditBox whose native document is already gone by the time it fires.
        // Stopping the timer here narrows the window; RefreshDetection's own try/catch closes it.
        PageClosing.Register(() => _detectTimer.Stop());

        _editor.TextChanged += (_, _) =>
        {
            if (_replacing)
                return;
            _isDirty = _editor.Text.Length > 0;
            StatusBar.SetCounts(_editor.Text);
            _detectTimer.Stop();
            _detectTimer.Start();
        };

        var options = OptionsBar();
        options.Children.Add(_detectedLabel);
        SetOptions(options);

        SetBody(Pane(_editor));
        SetFooter(_urlSafe);
        StatusBar.SetUntouched();
        InitializeChrome();
        RefreshDetection();
    }

    public override ToolKind Kind => ToolKind.Base64;
    public override bool IsDirty => _isDirty;

    public override IReadOnlyList<ToolCommand> Commands => _commands ??=
    [
        new ToolCommand(Loc.Get("Base64.Encode"), EncodeGlyph, Encode, id: "base64-encode"),
        new ToolCommand(Loc.Get("Base64.Decode"), DecodeGlyph, Decode, id: "base64-decode"),
        ToolCommand.Copy(CopyOutput),
        ToolCommand.Clear(Clear),
    ];

    public override bool AcceptsClipboardText(string text) =>
        text.Length > 0 && Base64Codec.IsLikelyBase64(text);

    public override void PasteClipboardText(string text)
    {
        _editor.Text = text;
        Decode();
    }

    public override async Task LoadFileAsync(string path)
    {
        var read = await InputLimits.ReadBytesAsync(path);
        if (!read.Ok)
        {
            StatusBar.SetMessage(read.Error, isError: true);
            return;
        }

        Replace(Convert.ToBase64String(read.Bytes));
        StatusBar.SetMessage(Loc.Format("Base64.FromFile", Path.GetFileName(path), InputLimits.Describe(read.Bytes.Length)), isError: false);
    }

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
        RefreshDetection();
    }

    private void Encode()
    {
        var text = _editor.Text;
        if (text.Length == 0)
            return;

        var result = Base64Codec.Encode(text, _urlSafe.IsChecked == true);
        Replace(result.Text);
        StatusBar.SetMessage(Loc.Get("Base64.Encoded"), isError: false);
    }

    private void Decode()
    {
        var text = _editor.Text;
        if (text.Length == 0)
            return;

        var result = Base64Codec.Decode(text);
        if (!result.Success)
        {
            StatusBar.SetMessage(result.Error, isError: true);
            return;
        }

        Replace(result.Text);
        StatusBar.SetMessage(Loc.Get("Base64.Decoded"), isError: false);
    }

    /// <summary>
    /// Runs on every keystroke's debounce tick, whose one caller is a dispatcher tick with no
    /// caller to catch what it throws — see <see cref="FormatterPage.RefreshDetection"/> for the
    /// identical guard and the closed-window race it exists for.
    /// </summary>
    private void RefreshDetection()
    {
        try
        {
            var text = _editor.Text;
            _detectedLabel.Text = text.Length == 0
                ? ""
                : Base64Codec.IsLikelyBase64(text) ? Loc.Get("Base64.DetectedBase64") : Loc.Get("Base64.DetectedText");
        }
        catch (Exception ex)
        {
            Diag.Log("Base64Page.RefreshDetection: document unavailable: " + ex.Message);
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
}
