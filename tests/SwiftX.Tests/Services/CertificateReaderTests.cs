using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

public class CertificateReaderTests
{
    [Fact]
    public void Read_DerCertificate_Succeeds()
    {
        using var cert = CreateSelfSigned("CN=der-test");
        var outcome = CertificateReader.Read(cert.Export(X509ContentType.Cert), null);

        Assert.True(outcome.Success);
        var entry = Assert.Single(outcome.Entries);
        Assert.Contains("der-test", entry.Certificate.Subject);
    }

    [Fact]
    public void Read_PemCertificate_Succeeds()
    {
        using var cert = CreateSelfSigned("CN=pem-test");
        var pem = cert.ExportCertificatePem();

        var outcome = CertificateReader.Read(Encoding.ASCII.GetBytes(pem), null);

        Assert.True(outcome.Success);
        Assert.Single(outcome.Entries);
    }

    [Fact]
    public void Read_Pkcs12WithoutPassword_ReportsNeedsPassword()
    {
        using var cert = CreateSelfSigned("CN=p12-test");
        var pfx = cert.Export(X509ContentType.Pfx, "correct-horse");

        var outcome = CertificateReader.Read(pfx, null);

        Assert.False(outcome.Success);
        Assert.True(outcome.NeedsPassword);
    }

    [Fact]
    public void Read_Pkcs12WithCorrectPassword_Succeeds()
    {
        using var cert = CreateSelfSigned("CN=p12-test");
        var pfx = cert.Export(X509ContentType.Pfx, "correct-horse");

        var outcome = CertificateReader.Read(pfx, "correct-horse");

        Assert.True(outcome.Success);
        Assert.Single(outcome.Entries);
    }

    [Fact]
    public void Read_Pkcs12WithWrongPassword_ReportsErrorWithoutCrashing()
    {
        using var cert = CreateSelfSigned("CN=p12-test");
        var pfx = cert.Export(X509ContentType.Pfx, "correct-horse");

        var outcome = CertificateReader.Read(pfx, "wrong-password");

        Assert.False(outcome.Success);
        Assert.False(string.IsNullOrEmpty(outcome.Message));
    }

    [Fact]
    public void Read_GarbageBytes_ReportsUnrecognizedWithoutCrashing()
    {
        var outcome = CertificateReader.Read([1, 2, 3, 4, 5], null);

        Assert.False(outcome.Success);
        Assert.False(outcome.NeedsPassword);
    }

    // The RSA key is deliberately left undisposed: CreateSelfSigned's resulting certificate can
    // depend on it staying alive for the private-key operations Export(Pfx, ...) performs below.
    private static X509Certificate2 CreateSelfSigned(string subject)
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }
}
