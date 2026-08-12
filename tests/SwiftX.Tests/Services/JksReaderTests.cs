using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

public class JksReaderTests
{
    [Fact]
    public void TryParse_NonKeystoreBytes_ReturnsNotAKeystore()
    {
        var result = JksReader.TryParse(Encoding.UTF8.GetBytes("just some random text, not a keystore"));
        Assert.False(result.IsKeyStore);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void TryParse_TooShort_ReturnsNotAKeystore()
    {
        var result = JksReader.TryParse([1, 2, 3]);
        Assert.False(result.IsKeyStore);
    }

    [Fact]
    public void TryParse_TrustedCertEntry_ReadsAliasAndCertificate()
    {
        var certDer = CreateSelfSignedDer("CN=jks-test");
        var jks = BuildJks(("my-alias", certDer));

        var result = JksReader.TryParse(jks);

        Assert.True(result.IsKeyStore);
        Assert.False(result.Truncated);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("my-alias", entry.Alias);
        Assert.Equal("Trusted certificate entry", entry.EntryType);
        var der = Assert.Single(entry.CertificateDer);
        Assert.Equal(certDer, der);
    }

    [Fact]
    public void TryParse_MultipleEntries_ReadsAllInOrder()
    {
        var certA = CreateSelfSignedDer("CN=a");
        var certB = CreateSelfSignedDer("CN=b");
        var jks = BuildJks(("alias-a", certA), ("alias-b", certB));

        var result = JksReader.TryParse(jks);

        Assert.True(result.IsKeyStore);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("alias-a", result.Entries[0].Alias);
        Assert.Equal("alias-b", result.Entries[1].Alias);
    }

    [Fact]
    public void TryParse_TruncatedFile_ReportsErrorInsteadOfThrowing()
    {
        var certDer = CreateSelfSignedDer("CN=jks-test");
        var jks = BuildJks(("alias", certDer));
        var truncated = jks[..(jks.Length - 10)];

        var result = JksReader.TryParse(truncated);

        Assert.True(result.IsKeyStore);
        Assert.True(result.Truncated);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    private static byte[] CreateSelfSignedDer(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return cert.Export(X509ContentType.Cert);
    }

    /// <summary>
    /// Hand-builds a minimal JKS byte stream — magic, version 2, count, then one
    /// TrustedCertEntry per (alias, der) pair — enough to exercise <see cref="JksReader"/>
    /// without a checked-in binary fixture.
    /// </summary>
    private static byte[] BuildJks(params (string Alias, byte[] Der)[] entries)
    {
        using var stream = new MemoryStream();
        WriteInt32(stream, unchecked((int)0xFEEDFEED));
        WriteInt32(stream, 2);
        WriteInt32(stream, entries.Length);

        foreach (var (alias, der) in entries)
        {
            WriteInt32(stream, 2); // TrustedCertEntry
            WriteJavaUtf(stream, alias);
            WriteInt64(stream, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            WriteJavaUtf(stream, "X.509");
            WriteInt32(stream, der.Length);
            stream.Write(der);
        }

        return stream.ToArray();
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteJavaUtf(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> lenBuffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lenBuffer, (ushort)bytes.Length);
        stream.Write(lenBuffer);
        stream.Write(bytes);
    }
}
