using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
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
        var scroller = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16, 12, 16, 16),
        };

        var body = Pane(scroller);
        body.AllowDrop = true;
        body.DragOver += Body_DragOver;
        body.DragEnter += Body_DragEnter;
        body.DragLeave += Body_DragLeave;
        body.Drop += Body_Drop;

        SetBody(body);
        SetFooter();
        StatusBar.SetUntouched();
        InitializeChrome();
        RenderEmpty();

        // The host cancels this when the window closes (see ToolPage.NotifyClosing), which is the
        // sanctioned place for a page to let go of what it owns. Certificates and the keystore
        // bytes behind them should not outlive the window that was showing them.
        PageClosing.Register(ReleaseLoadedFile);
    }

    public override ToolKind Kind => ToolKind.Certificate;
    public override bool IsDirty => false;

    public override IReadOnlyList<ToolCommand> Commands => _commands ??=
    [
        new ToolCommand(Loc.Get("Certificate.OpenFile"), OpenFileGlyph, () => _ = OpenFileAsync(), id: "cert-open"),
        ToolCommand.Copy(CopyPem),
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

    private void Body_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border border && e.DataView.Contains(StandardDataFormats.StorageItems))
            border.BorderBrush = ThemeBrush("AccentFillColorDefaultBrush") ?? border.BorderBrush;
    }

    private void Body_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
            border.BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush");
    }

    private void Body_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
            e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Body_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
            border.BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush");

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.OfType<StorageFile>().FirstOrDefault() is { } file)
                await LoadFileAsync(file.Path);
        }
        catch (Exception ex)
        {
            Diag.Log("CertificatePage.Body_Drop failed: " + ex);
            StatusBar.SetMessage(Loc.Get("Tool.FileUnreadable"), isError: true);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Process(string? password)
    {
        if (_lastBytes is null)
            return;
        Render(CertificateReader.Read(_lastBytes, password));
    }

    /// <summary>
    /// Shows an outcome and takes ownership of its certificates, releasing whatever the page was
    /// holding before. Every path into here replaces <see cref="_lastOutcome"/>, and a
    /// certificate owns a native <c>CERT_CONTEXT</c> (and, for a PKCS#12, live private-key
    /// material) — see <see cref="CertificateReader.ReadOutcome"/> for why letting the old one
    /// fall to the finalizer is not good enough. The retry loop is what makes this reachable
    /// rather than theoretical: every wrong password on a keystore produces another full set.
    /// </summary>
    private void Render(CertificateReader.ReadOutcome outcome)
    {
        _content.Children.Clear();

        if (!ReferenceEquals(_lastOutcome, outcome))
            _lastOutcome?.Dispose();

        if (!outcome.Success)
        {
            _lastOutcome = null;
            outcome.Dispose();
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
        ApplyHoverIconColor(unlockButton, HoverAccent.Accent);

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

    private enum ValidityState { Valid, Expired, NotYetValid }

    private static ValidityState GetValidityState(X509Certificate2 cert)
    {
        var now = DateTime.Now;
        if (now < cert.NotBefore) return ValidityState.NotYetValid;
        if (now > cert.NotAfter) return ValidityState.Expired;
        return ValidityState.Valid;
    }

    private FrameworkElement BuildCard(CertificateReader.CertEntry entry)
    {
        var cert = entry.Certificate;
        var state = GetValidityState(cert);
        var stack = new StackPanel { Spacing = 10 };

        var titleRow = new Grid { ColumnSpacing = 8 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = entry.Alias ?? cert.Subject,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(titleBlock, 0);
        titleRow.Children.Add(titleBlock);

        // The type pill sits left of the valid/invalid badge, which is always present now (a
        // green check or a red X), so its position never jumps between certificates.
        var typePill = BuildTypePill(entry.Source);
        Grid.SetColumn(typePill, 1);
        titleRow.Children.Add(typePill);

        var badge = BuildValidityBadge(state, cert);
        Grid.SetColumn(badge, 2);
        titleRow.Children.Add(badge);

        stack.Children.Add(titleRow);
        stack.Children.Add(BuildForm(cert, state));

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

    /// <summary>
    /// The label/value pairs (including the CN convenience rows and a trailing countdown row —
    /// "Expires in", "Expired", or "Starts in" depending on <paramref name="state"/>) laid out as
    /// a form: labels on the left, values on the right, on a background a step apart from the
    /// card that hosts it so it reads as its own section rather than a continuation of the title
    /// row above. Valid To carries its own check/cross treatment (see
    /// <see cref="DetailRowWithValidity"/>) since it's the one field that answers "is this
    /// certificate still good?" on its own; Valid From only turns red when it's the boundary
    /// that's actually the problem (not yet valid) — the badge already says "something's wrong",
    /// this says which field.
    /// </summary>
    private FrameworkElement BuildForm(X509Certificate2 cert, ValidityState state)
    {
        string commonNameLabel = Loc.Get("Certificate.Field.CommonName");
        string notAfterLabel = Loc.Get("Certificate.Field.NotAfter");
        string highlightLabel = state == ValidityState.NotYetValid ? Loc.Get("Certificate.Field.NotBefore") : "";
        bool currentlyValid = state == ValidityState.Valid;

        var rows = DescribeFields(cert)
            .Select(f => f.Label == commonNameLabel
                ? DetailRowWithCopy(f.Label, f.Value)
                : f.Label == notAfterLabel
                    ? DetailRowWithValidity(f.Label, f.Value, currentlyValid)
                    : DetailRow(f.Label, f.Value, highlightRed: f.Label == highlightLabel))
            .ToList();

        var now = DateTime.Now;
        var (countdownLabel, countdownValue) = state switch
        {
            ValidityState.Valid => (Loc.Get("Certificate.Field.ExpiresIn"), Loc.Format("Certificate.ExpiresInValue", (int)(cert.NotAfter - now).TotalDays)),
            ValidityState.Expired => (Loc.Get("Certificate.Field.Expired"), Loc.Format("Certificate.ExpiredValue", (int)(now - cert.NotAfter).TotalDays)),
            _ => (Loc.Get("Certificate.Field.StartsIn"), Loc.Format("Certificate.ExpiresInValue", (int)(cert.NotBefore - now).TotalDays)),
        };
        rows.Add(DetailRow(countdownLabel, countdownValue));

        var form = new StackPanel();
        for (int i = 0; i < rows.Count; i++)
            form.Children.Add(FormRow(rows[i], isLast: i == rows.Count - 1));

        return new Border
        {
            Child = form,
            Padding = new Thickness(12, 0, 12, 0),
            Background = ThemeBrush("CardBackgroundFillColorSecondaryBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
    }

    private static FrameworkElement FormRow(FrameworkElement content, bool isLast) => new Border
    {
        Child = content,
        Padding = new Thickness(0, 8, 0, 8),
        BorderThickness = isLast ? new Thickness(0) : new Thickness(0, 0, 0, 1),
        BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush") ?? ThemeBrush("CardStrokeColorDefaultBrush"),
    };

    private static FrameworkElement BuildTypePill(string text) => new Border
    {
        Child = new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        },
        Background = ThemeBrush("ControlFillColorSecondaryBrush"),
        BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(8, 2, 8, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    // Built from the codepoint, not a literal character in source — see the memory note on PUA
    // glyph corruption from a stray copy/paste. The X is the same glyph SystemRingItems already
    // uses for the ring's "Close" item, so it's a codepoint this app already ships and renders.
    private static readonly string ValidCheckGlyph = char.ConvertFromUtf32(0xE8FB);
    private static readonly string InvalidGlyph = char.ConvertFromUtf32(0xE711);

    private static FrameworkElement BuildValidityBadge(ValidityState state, X509Certificate2 cert)
    {
        bool valid = state == ValidityState.Valid;
        var icon = new FontIcon
        {
            Glyph = valid ? ValidCheckGlyph : InvalidGlyph,
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = valid
                ? ThemeBrush("SystemFillColorSuccessBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Green)
                : ThemeBrush("SystemFillColorCriticalBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Red),
        };
        var badge = new Border
        {
            Child = icon,
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = valid
                ? ThemeBrush("SystemFillColorSuccessBackgroundBrush")
                : ThemeBrush("SystemFillColorCriticalBackgroundBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The green badge's caption is a fixed "currently valid"; the red one reuses
        // ValidityStatus's own message so the tooltip says exactly why (expired N days ago, or
        // not yet valid until a given date) instead of a generic "invalid".
        var label = valid ? Loc.Get("Certificate.ValidBadge") : ValidityStatus(cert);
        ToolTipService.SetToolTip(badge, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge, label);
        return badge;
    }

    private static FrameworkElement DetailRow(string label, string value, bool highlightRed = false)
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
        if (highlightRed)
            valueBlock.Foreground = ThemeBrush("SystemFillColorCriticalBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Red);

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        return grid;
    }

    /// <summary>Same shape as <see cref="DetailRow"/>, for the Valid To row alone — carries the
    /// same green-check/red-cross treatment as <see cref="BuildValidityBadge"/>'s title-row badge
    /// (same glyphs, same theme brushes), just inline with the date itself rather than off in the
    /// corner, since this is the one field that says on its own whether the certificate still
    /// works.</summary>
    /// <summary>Same shape as <see cref="DetailRow"/>, for the Valid To row alone — the date
    /// reads first, exactly like every other field, with a small pill badge <em>after</em> it
    /// carrying the check/cross: same glyphs and theme brushes as <see cref="BuildValidityBadge"/>'s
    /// title-row badge, just an inline pill instead of a circle, since this one sits beside text
    /// rather than beside the type tag.</summary>
    private static FrameworkElement DetailRowWithValidity(string label, string value, bool valid)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock { Text = label, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var badgeForeground = valid
            ? ThemeBrush("SystemFillColorSuccessBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Green)
            : ThemeBrush("SystemFillColorCriticalBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Red);
        var badgeBackground = valid
            ? ThemeBrush("SystemFillColorSuccessBackgroundBrush")
            : ThemeBrush("SystemFillColorCriticalBackgroundBrush");

        var icon = new FontIcon
        {
            Glyph = valid ? ValidCheckGlyph : InvalidGlyph,
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            FontSize = 11,
            Foreground = badgeForeground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new Border
        {
            Child = icon,
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = badgeBackground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        valueRow.Children.Add(valueBlock);
        valueRow.Children.Add(badge);

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueRow, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueRow);
        return grid;
    }

    // Built from the codepoint, not a literal character in source — see the memory note on PUA
    // glyph corruption from a stray copy/paste.
    private static readonly string CopyGlyph = char.ConvertFromUtf32(0xE8C8);

    /// <summary>Same shape as <see cref="DetailRow"/>, with a copy button for the CN convenience
    /// rows — the one field on the card worth lifting straight to the clipboard on its own,
    /// rather than the whole card's summary via <see cref="CopySummary"/>.</summary>
    private FrameworkElement DetailRowWithCopy(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock { Text = label, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var copyButton = new Button
        {
            Content = new FontIcon { Glyph = CopyGlyph, FontSize = 12 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(copyButton, Loc.Get("Tool.CopyOutput"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copyButton, Loc.Format("Certificate.CopyField", label));
        ApplyHoverIconColor(copyButton, HoverAccent.Copy);
        copyButton.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(value);
            Clipboard.SetContent(package);
            StatusBar.SetMessage(Loc.Get("Certificate.Copied"), isError: false);
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        Grid.SetColumn(copyButton, 2);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        grid.Children.Add(copyButton);
        return grid;
    }

    // The commonName (2.5.4.3) attribute, read from the structured RDN sequence rather than
    // guessed out of the formatted DN string — X509NameType.SimpleName (used by FriendlyAlias
    // below) silently falls back to OU/O/E when no CN is present, which is the wrong answer for a
    // row whose whole point is "only the CN value, or nothing".
    private const string CommonNameOid = "2.5.4.3";

    private static string? CommonName(X500DistinguishedName name)
    {
        foreach (var rdn in name.EnumerateRelativeDistinguishedNames())
        {
            if (!rdn.HasMultipleElements && rdn.GetSingleElementType().Value == CommonNameOid)
                return rdn.GetSingleElementValue();
        }
        return null;
    }

    /// <summary>
    /// The label/value pairs shown on a card and copied to the clipboard — one source of
    /// truth for both, computed from the certificate rather than duplicated between the UI
    /// and <see cref="CopySummary"/>.
    /// </summary>
    private static IEnumerable<(string Label, string Value)> DescribeFields(X509Certificate2 cert)
    {
        yield return (Loc.Get("Certificate.Field.Subject"), cert.Subject);
        if (CommonName(cert.SubjectName) is { Length: > 0 } subjectCn)
            yield return (Loc.Get("Certificate.Field.CommonName"), subjectCn);
        yield return (Loc.Get("Certificate.Field.Issuer"), cert.Issuer);
        if (CommonName(cert.IssuerName) is { Length: > 0 } issuerCn)
            yield return (Loc.Get("Certificate.Field.CommonName"), issuerCn);
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

    /// <summary>Copies every loaded certificate as PEM text (<c>-----BEGIN CERTIFICATE-----</c>
    /// blocks, one per entry) — the portable form someone reaches for this tool to grab, rather
    /// than the human-readable field summary the card itself already shows on screen.</summary>
    private void CopyPem()
    {
        if (_lastOutcome is not { Entries.Count: > 0 } outcome)
            return;

        var pem = string.Join(
            Environment.NewLine,
            outcome.Entries.Select(entry => entry.Certificate.ExportCertificatePem()));

        var package = new DataPackage();
        package.SetText(pem);
        Clipboard.SetContent(package);
        StatusBar.SetMessage(Loc.Get("Tool.CopyOutput"), isError: false);
    }

    private void ClearAll()
    {
        ReleaseLoadedFile();
        RenderEmpty();
        StatusBar.SetUntouched();
    }

    /// <summary>
    /// Drops everything derived from the file the user opened: the certificates (and, for a
    /// PKCS#12, the private keys they carry) and the raw bytes the page kept so a password could
    /// be retried against them. Those bytes are the encrypted keystore itself, so holding them
    /// for the life of the window after the user has pressed Clear — or closed it — is a
    /// needlessly long residency for exactly the thing this tool exists to handle carefully.
    /// Idempotent: safe on a page that never loaded a file, and safe to call twice.
    /// </summary>
    private void ReleaseLoadedFile()
    {
        _lastOutcome?.Dispose();
        _lastOutcome = null;

        if (_lastBytes is { Length: > 0 } bytes)
            Array.Clear(bytes);
        _lastBytes = null;
    }

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
