using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Security;

/// <summary>
/// How long sensitive material stays alive in the process, and where a copied secret is allowed
/// to travel.
/// <para>
/// The rest of the security suite is about refusing hostile input. This file is about the other
/// half of a security tool's job: not leaving the material it was trusted with lying around
/// longer, or somewhere wider, than the user asked for. Both defects it covers were silent —
/// nothing failed, nothing was refused, and the app behaved correctly in every visible way while
/// certificates accumulated native handles and every copied password was queued for sync to the
/// user's other devices.
/// </para>
/// </summary>
public class SecretLifetimeTests
{
    // =========================================================================================
    // 1. Certificate and private-key lifetime
    // =========================================================================================

    /// <summary>
    /// A read outcome owns its certificates and releases them on demand.
    /// <para>
    /// Each <see cref="X509Certificate2"/> wraps a native <c>CERT_CONTEXT</c>: unmanaged memory
    /// the GC cannot see and therefore feels no pressure to reclaim. The Certificate tool replaces
    /// its outcome on every load and on every wrong-password retry, so without an owner calling
    /// this those contexts survive until the finalizer queue happens to notice. Disposal is
    /// asserted through the certificate's own post-disposal behaviour rather than through a handle
    /// count, because the handle is exactly the thing a managed test cannot see.
    /// </para>
    /// </summary>
    [Fact]
    public void Disposing_an_outcome_releases_every_certificate_in_it()
    {
        using var source = CreateSelfSigned("CN=lifetime-test");
        var outcome = CertificateReader.Read(source.Export(X509ContentType.Cert), null);

        Assert.True(outcome.Success);
        var cert = Assert.Single(outcome.Entries).Certificate;
        Assert.Contains("lifetime-test", cert.Subject); // usable before disposal

        outcome.Dispose();

        AssertHandleReleased(cert);
    }

    /// <summary>
    /// A PKCS#12 is the case that matters most: what it carries is not just a handle but live
    /// private-key material, loaded ephemerally so it never touches a key store but resident in
    /// this process until it is released. Dropping the reference and waiting for a finalizer is
    /// not the same promise as releasing it when the user closed the window.
    /// </summary>
    [Fact]
    public void Disposing_an_outcome_releases_private_key_material()
    {
        using var source = CreateSelfSigned("CN=pfx-lifetime");
        var pfx = source.Export(X509ContentType.Pfx, "correct-horse");

        var outcome = CertificateReader.Read(pfx, "correct-horse");
        Assert.True(outcome.Success);

        Assert.NotEmpty(outcome.Entries);
        var cert = outcome.Entries[0].Certificate;
        Assert.True(cert.HasPrivateKey);

        outcome.Dispose();

        AssertHandleReleased(cert);
    }

    /// <summary>
    /// Disposal is idempotent. The page disposes when it replaces an outcome and again when the
    /// window closes, and those two can be the same outcome — a second call throwing would turn
    /// tidy-up into an exception on a close path, which is where there is no caller to catch it.
    /// </summary>
    [Fact]
    public void Disposing_an_outcome_twice_is_safe()
    {
        using var source = CreateSelfSigned("CN=double-dispose");
        var outcome = CertificateReader.Read(source.Export(X509ContentType.Cert), null);

        outcome.Dispose();
        outcome.Dispose();
    }

    /// <summary>
    /// A failed read has nothing to release, and disposing it must still be a no-op rather than a
    /// null dereference — the failure paths hand back an outcome with an empty entry list, and
    /// the page disposes whatever it is holding without inspecting it first.
    /// </summary>
    [Fact]
    public void Disposing_a_failed_outcome_is_a_no_op()
    {
        var outcome = CertificateReader.Read([1, 2, 3, 4, 5], null);

        Assert.False(outcome.Success);
        Assert.Empty(outcome.Entries);
        outcome.Dispose();
    }

    // =========================================================================================
    // 2. Where a copied secret is allowed to go
    // =========================================================================================

    /// <summary>
    /// A copied secret is marked so the shell keeps it out of clipboard history and out of cloud
    /// roaming.
    /// <para>
    /// This is the opt-out that stops Windows doing two things by default with anything placed on
    /// the clipboard: retaining it in the Win+V history list, and syncing it to every device
    /// signed in to the same Microsoft account. Secure values are DPAPI-encrypted at rest
    /// specifically so they never leave this account on this machine; handing them to cloud
    /// clipboard on Copy undid that, quietly, at the exact moment the user reached for the value.
    /// </para>
    /// <para>
    /// The property names are what the shell reads, so they are asserted literally: a typo here
    /// fails open — the payload is copied normally and roams normally, with nothing to see.
    /// </para>
    /// </summary>
    [Fact]
    public void A_secret_payload_is_marked_out_of_clipboard_history_and_roaming()
    {
        var properties = new Dictionary<string, object>();

        SecretClipboard.MarkSecret(properties);

        Assert.False((bool)properties["CanIncludeInClipboardHistory"]);
        Assert.False((bool)properties["CanUploadToCloudClipboard"]);
    }

    /// <summary>The constants and the literal names above must agree — the assertion is only
    /// worth anything if callers use the same strings the shell does.</summary>
    [Fact]
    public void The_opt_out_property_names_are_the_ones_the_shell_reads()
    {
        Assert.Equal("CanIncludeInClipboardHistory", SecretClipboard.HistoryProperty);
        Assert.Equal("CanUploadToCloudClipboard", SecretClipboard.RoamingProperty);
    }

    /// <summary>
    /// Marking a package that already carries either key must leave it opted out, not throw. A
    /// data package is built up by its caller, so the marker cannot assume it is the first writer
    /// — and an <c>Add</c> on a duplicate key would throw from inside a Copy button's handler.
    /// </summary>
    [Fact]
    public void Marking_a_payload_that_is_already_marked_is_not_an_error()
    {
        var properties = new Dictionary<string, object>
        {
            [SecretClipboard.HistoryProperty] = true,
            [SecretClipboard.RoamingProperty] = true,
        };

        SecretClipboard.MarkSecret(properties);

        Assert.False((bool)properties[SecretClipboard.HistoryProperty]);
        Assert.False((bool)properties[SecretClipboard.RoamingProperty]);
    }

    /// <summary>
    /// Asserts a certificate's native context is gone, by the only signal a managed test has: a
    /// member that reads through it now fails.
    /// <para>
    /// Either exception proves the point and the framework has used both over time —
    /// <see cref="CryptographicException"/> from the handle validity check, or
    /// <see cref="ObjectDisposedException"/> from a disposal check ahead of it. Pinning this to
    /// one of them would make the test a statement about which check runs first rather than about
    /// whether the handle was released.
    /// </para>
    /// </summary>
    private static void AssertHandleReleased(X509Certificate2 cert)
    {
        var thrown = Record.Exception(() => _ = cert.RawData);

        Assert.True(
            thrown is CryptographicException or ObjectDisposedException,
            $"expected the certificate's native context to be released; got {thrown?.GetType().Name ?? "no exception"}");
    }

    // The RSA key is deliberately left undisposed, matching CertificateReaderTests: the resulting
    // certificate can depend on it staying alive for the private-key operations Export performs.
    private static X509Certificate2 CreateSelfSigned(string subject)
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }
}
