using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Stress and resilience testing for SecureValueStore, CertificateReader, and InputLimits.
/// </summary>
public class SecureValuesAndCertStressTests
{
    // =========================================================================
    // 1. SECURE VALUES BATCH & LARGE VALUE STRESS
    // =========================================================================

    [Fact]
    public void SecureValueStore_BatchOperations_EncryptsAndDecryptsAccurately()
    {
        // Generate 1000 key-value pairs including large keys, unicode, and large multiline secrets
        var entries = new List<SecureValueEntry>();
        for (int i = 0; i < 1000; i++)
        {
            entries.Add(new SecureValueEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Key = $"KEY_SECRET_{i}_{Guid.NewGuid():N}",
                Value = $"Secret_Value_{i}_café_🚀_{new string('X', i % 500)}"
            });
        }

        // Save
        var sw = Stopwatch.StartNew();
        SecureValueStore.Save(entries);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Saving 1000 encrypted entries took {sw.ElapsedMilliseconds}ms");

        // Load and verify
        var loaded = SecureValueStore.Load();
        Assert.Equal(entries.Count, loaded.Count);

        var loadedDict = loaded.ToDictionary(e => e.Key, e => e.Value);
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.True(loadedDict.TryGetValue(entries[i].Key, out var val));
            Assert.Equal(entries[i].Value, val);
        }
    }

    // =========================================================================
    // 2. CERTIFICATE READER STRESS & FUZZING
    // =========================================================================

    [Fact]
    public void CertificateReader_LargePemBundle_ParsesAllEntries()
    {
        // Generate a multi-cert PEM bundle using ephemeral self-signed certs
        var pemBuilder = new StringBuilder();
        var expectedSubjects = new List<string>();

        using var rsa = RSA.Create(2048);
        for (int i = 0; i < 15; i++)
        {
            string cn = $"CN=SwiftX Test Cert {i}";
            expectedSubjects.Add(cn);
            var req = new CertificateRequest(cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-10), DateTimeOffset.Now.AddDays(365));
            pemBuilder.AppendLine(cert.ExportCertificatePem());
        }

        byte[] pemBytes = Encoding.UTF8.GetBytes(pemBuilder.ToString());
        var outcome = CertificateReader.Read(pemBytes, password: null);

        Assert.True(outcome.Success);
        Assert.Equal(15, outcome.Entries.Count);
        for (int i = 0; i < outcome.Entries.Count; i++)
        {
            Assert.Equal(expectedSubjects[i], outcome.Entries[i].Certificate.Subject);
        }
    }

    [Fact]
    public void CertificateReader_FuzzedCorruptBinary_NeverCrashes()
    {
        var random = new Random(12345);

        // Feed corrupted byte sequences of varying sizes
        for (int iteration = 0; iteration < 50; iteration++)
        {
            int size = random.Next(1, 10000);
            byte[] garbage = new byte[size];
            random.NextBytes(garbage);

            var outcome = CertificateReader.Read(garbage, password: null);
            // Must return outcome with Success = false or handle gracefully, never throw
            Assert.NotNull(outcome);
            if (!outcome.Success)
            {
                Assert.NotNull(outcome.Message);
            }
        }
    }

    // =========================================================================
    // 3. INPUT LIMITS BOUNDARY CHECKS
    // =========================================================================

    [Fact]
    public async Task InputLimits_RefusesOversizedFiles_WithoutReadingWholeFile()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"swiftx_oversize_test_{Guid.NewGuid():N}.dat");
        try
        {
            // Create a sparse/virtual file larger than MaxTextBytes (50 MB + 1024 bytes)
            long targetSize = InputLimits.MaxTextBytes + 1024;
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.SetLength(targetSize);
            }

            var result = await InputLimits.ReadTextAsync(tempPath);
            Assert.False(result.Ok);
            Assert.NotEmpty(result.Error);
            Assert.Contains("50 MB", result.Error);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}

