using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SwiftX.ToolPages;

/// <summary>
/// Examines a certificate, PKCS#12/PFX, or Java KeyStore file and lists every certificate it
/// contains with its full details. Read-only: nothing is edited or saved, so
/// <see cref="IsDirty"/> is always false and there is no discard prompt on close.
/// </summary>
public sealed class CertificatePage : EditorToolPage
{
    // Built from the codepoint rather than a literal PUA character in source — see the same
    // note on Base64Page's glyph fields.
    private static readonly string OpenFileGlyph = char.ConvertFromUtf32(0xE8E5);

    private readonly StackPanel _content = new() { Spacing = 12 };
    private IReadOnlyList<ToolCommand>? _commands;
    private byte[]? _lastBytes;
    private CertificateReader.ReadOutcome? _lastOutcome;

    public CertificatePage()
    {
        var header = new TextBlock
        {
            Text = Loc.Get("Certificate.Blurb"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var scroller = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16, 12, 16, 16),
        };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var headerBorder = new Border { Child = header, Padding = new Thickness(16, 12, 16, 0) };
        Grid.SetRow(headerBorder, 0);
        Grid.SetRow(scroller, 1);
        body.Children.Add(headerBorder);
        body.Children.Add(scroller);

        SetBody(Pane(body));
        StatusBar.SetUntouched();
        InitializeChrome();
        RenderEmpty();
    }

    public override ToolKind Kind => ToolKind.Certificate;
    public override bool IsDirty => false;

    public override IReadOnlyList<ToolCommand> Commands => _commands ??=
    [
        new ToolCommand(Loc.Get("Certificate.OpenFile"), OpenFileGlyph, () => _ = OpenFileAsync(), id: "cert-open"),
        ToolCommand.Copy(CopySummary),
        ToolCommand.Clear(ClearAll),
    ];

    public override async Task LoadFileAsync(string path)
    {
        var read = await InputLimits.ReadBytesAsync(path);
        if (!read.Ok)
        {
            StatusBar.SetMessage(read.Error, isError: true);
            return;
        }

        _lastBytes = read.Bytes;
        Process(password: null);
    }

    private async Task OpenFileAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, HostHwnd);
            foreach (var ext in new[] { ".cer", ".crt", ".der", ".pem", ".p7b", ".p7c", ".p12", ".pfx", ".jks", ".keystore" })
                picker.FileTypeFilter.Add(ext);
            picker.FileTypeFilter.Add("*");
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;
            await LoadFileAsync(file.Path);
        }
        catch (Exception ex)
        {
            Diag.Log("CertificatePage.OpenFileAsync failed: " + ex);
            StatusBar.SetMessage(Loc.Get("Tool.FileUnreadable"), isError: true);
        }
    }

    private void Process(string? password)
    {
        if (_lastBytes is null)
            return;
        Render(CertificateReader.Read(_lastBytes, password));
    }

    private void Render(CertificateReader.ReadOutcome outcome)
    {
        _content.Children.Clear();

        if (!outcome.Success)
        {
            _lastOutcome = null;
            if (outcome.NeedsPassword)
                RenderPasswordPrompt(outcome.Message);
            else
                _content.Children.Add(new TextBlock { Text = outcome.Message ?? Loc.Get("Certificate.Unrecognized"), TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
            StatusBar.SetMessage(outcome.Message ?? "", isError: true);
            return;
        }

        _lastOutcome = outcome;
        if (!string.IsNullOrEmpty(outcome.Message))
            _content.Children.Add(new TextBlock { Text = outcome.Message, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });

        foreach (var entry in outcome.Entries)
            _content.Children.Add(BuildCard(entry));

        StatusBar.SetMessage(Loc.Format("Certificate.Count", outcome.Entries.Count), isError: false);
    }

    private void RenderEmpty()
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock
        {
            Text = Loc.Get("Certificate.Empty"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        });
    }

    private void RenderPasswordPrompt(string? message)
    {
        var stack = new StackPanel { Spacing = 8, MaxWidth = 420 };
        if (!string.IsNullOrEmpty(message))
            stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });

        var passwordBox = new PasswordBox { PlaceholderText = Loc.Get("Certificate.PasswordPlaceholder"), Width = 240 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(passwordBox, Loc.Get("Certificate.PasswordPlaceholder"));
        var unlockButton = new Button { Content = Loc.Get("Certificate.Unlock"), VerticalAlignment = VerticalAlignment.Center };

        void Submit() => Process(passwordBox.Password);
        unlockButton.Click += (_, _) => Submit();
        passwordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Submit();
                e.Handled = true;
            }
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(passwordBox);
        row.Children.Add(unlockButton);
        stack.Children.Add(row);
        _content.Children.Add(stack);
    }

    private FrameworkElement BuildCard(CertificateReader.CertEntry entry)
    {
        var cert = entry.Certificate;
        var stack = new StackPanel { Spacing = 6 };

        stack.Children.Add(new TextBlock
        {
            Text = entry.Alias ?? cert.Subject,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        stack.Children.Add(new TextBlock { Text = entry.Source, Opacity = 0.6 });
        stack.Children.Add(new TextBlock { Text = ValidityStatus(cert), Opacity = 0.85 });

        foreach (var (label, value) in DescribeFields(cert))
            stack.Children.Add(DetailRow(label, value));

        return new Border
        {
            Child = stack,
            Padding = new Thickness(14, 12, 14, 12),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
    }

    private static FrameworkElement DetailRow(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock { Text = label, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap };
        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        return grid;
    }

    /// <summary>
    /// The label/value pairs shown on a card and copied to the clipboard — one source of
    /// truth for both, computed from the certificate rather than duplicated between the UI
    /// and <see cref="CopySummary"/>.
    /// </summary>
    private static IEnumerable<(string Label, string Value)> DescribeFields(X509Certificate2 cert)
    {
        yield return (Loc.Get("Certificate.Field.Subject"), cert.Subject);
        yield return (Loc.Get("Certificate.Field.Issuer"), cert.Issuer);
        yield return (Loc.Get("Certificate.Field.Serial"), cert.SerialNumber);
        yield return (Loc.Get("Certificate.Field.Version"), cert.Version.ToString(CultureInfo.InvariantCulture));
        yield return (Loc.Get("Certificate.Field.NotBefore"), cert.NotBefore.ToLocalTime().ToString("f", CultureInfo.CurrentCulture));
        yield return (Loc.Get("Certificate.Field.NotAfter"), cert.NotAfter.ToLocalTime().ToString("f", CultureInfo.CurrentCulture));
        yield return (Loc.Get("Certificate.Field.SignatureAlgorithm"), cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "");
        yield return (Loc.Get("Certificate.Field.PublicKey"), PublicKeySummary(cert));
        yield return (Loc.Get("Certificate.Field.Sha1"), cert.Thumbprint);
        yield return (Loc.Get("Certificate.Field.Sha256"), Convert.ToHexString(SHA256.HashData(cert.RawData)));
        yield return (Loc.Get("Certificate.Field.HasPrivateKey"), cert.HasPrivateKey ? Loc.Get("Common.Yes") : Loc.Get("Common.No"));

        foreach (var extension in cert.Extensions)
        {
            string? label = null;
            string? value = null;
            switch (extension)
            {
                case X509KeyUsageExtension ku:
                    label = Loc.Get("Certificate.Field.KeyUsage");
                    value = ku.KeyUsages.ToString();
                    break;
                case X509EnhancedKeyUsageExtension eku:
                    label = Loc.Get("Certificate.Field.EnhancedKeyUsage");
                    value = string.Join(", ", eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o.FriendlyName ?? o.Value));
                    break;
                case X509BasicConstraintsExtension bc:
                    label = Loc.Get("Certificate.Field.BasicConstraints");
                    var ca = bc.CertificateAuthority ? "CA:TRUE" : "CA:FALSE";
                    value = bc.HasPathLengthConstraint ? $"{ca}, pathlen:{bc.PathLengthConstraint}" : ca;
                    break;
                case X509SubjectAlternativeNameExtension san:
                    var names = san.EnumerateDnsNames().Select(n => "DNS:" + n)
                        .Concat(san.EnumerateIPAddresses().Select(ip => "IP:" + ip))
                        .ToList();
                    if (names.Count > 0)
                    {
                        label = Loc.Get("Certificate.Field.SubjectAltNames");
                        value = string.Join(", ", names);
                    }
                    break;
                case X509SubjectKeyIdentifierExtension ski:
                    label = Loc.Get("Certificate.Field.SubjectKeyId");
                    value = ski.SubjectKeyIdentifier;
                    break;
                case X509AuthorityKeyIdentifierExtension { KeyIdentifier: { } id }:
                    label = Loc.Get("Certificate.Field.AuthorityKeyId");
                    value = Convert.ToHexString(id.Span);
                    break;
            }
            if (label is not null && !string.IsNullOrEmpty(value))
                yield return (label, value);
        }
    }

    private static string PublicKeySummary(X509Certificate2 cert)
    {
        var name = cert.PublicKey.Oid.FriendlyName ?? cert.PublicKey.Oid.Value ?? "";
        int? bits = cert.PublicKey.GetRSAPublicKey()?.KeySize
            ?? cert.PublicKey.GetECDsaPublicKey()?.KeySize
            ?? cert.PublicKey.GetDSAPublicKey()?.KeySize;
        return bits is { } b ? $"{name} ({b}-bit)" : name;
    }

    private static string ValidityStatus(X509Certificate2 cert)
    {
        var now = DateTime.Now;
        if (now < cert.NotBefore)
            return Loc.Format("Certificate.NotYetValid", cert.NotBefore.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
        if (now > cert.NotAfter)
            return Loc.Format("Certificate.Expired", (int)(now - cert.NotAfter).TotalDays);
        return Loc.Format("Certificate.ExpiresIn", (int)(cert.NotAfter - now).TotalDays);
    }

    private void CopySummary()
    {
        if (_lastOutcome is not { Entries.Count: > 0 } outcome)
            return;

        var blocks = outcome.Entries.Select(entry =>
        {
            var lines = new List<string> { entry.Alias ?? entry.Certificate.Subject };
            lines.AddRange(DescribeFields(entry.Certificate).Select(f => $"{f.Label}: {f.Value}"));
            return string.Join(Environment.NewLine, lines);
        });

        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine + Environment.NewLine, blocks));
        Clipboard.SetContent(package);
        StatusBar.SetMessage(Loc.Get("Tool.CopyOutput"), isError: false);
    }

    private void ClearAll()
    {
        _lastBytes = null;
        _lastOutcome = null;
        RenderEmpty();
        StatusBar.SetUntouched();
    }

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
