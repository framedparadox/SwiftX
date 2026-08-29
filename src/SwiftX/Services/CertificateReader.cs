using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SwiftX.Services;

/// <summary>
/// One entry point for "what's in this file": a certificate/chain (.cer/.crt/.der/.pem), a
/// PKCS#7 bundle (.p7b/.p7c), a password-protected PKCS#12/PFX (.p12/.pfx), or a Java
/// KeyStore (.jks/.keystore, via <see cref="JksReader"/>). Never throws — every failure mode
/// (unrecognized format, missing/wrong password, corrupt file) comes back as a result the
/// page can show, not an exception the page has to guard against.
/// </summary>
public static class CertificateReader
{
    public sealed record CertEntry(string? Alias, string Source, X509Certificate2 Certificate);

    public sealed record ReadOutcome(bool Success, bool NeedsPassword, string? Message, IReadOnlyList<CertEntry> Entries, string ContainerKind)
    {
        public static ReadOutcome Failed(string message, bool needsPassword = false) =>
            new(false, needsPassword, message, [], "");
    }

    public static ReadOutcome Read(byte[] data, string? password)
    {
        var jks = JksReader.TryParse(data);
        if (jks.IsKeyStore)
            return ReadJks(jks);

        if (IsPkcs12(data))
            return ReadPkcs12(data, password);

        return TryGenericImport(data) ?? ReadOutcome.Failed(Loc.Get("Certificate.Unrecognized"));
    }

    private static bool IsPkcs12(byte[] data)
    {
        try
        {
            // Pfx and Pkcs12 are the same numeric value — this one check covers both names.
            return X509Certificate2.GetCertContentType(data) == X509ContentType.Pkcs12;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static ReadOutcome ReadJks(JksReader.Result jks)
    {
        var entries = new List<CertEntry>();
        foreach (var entry in jks.Entries)
        {
            foreach (var der in entry.CertificateDer)
            {
                try
                {
                    entries.Add(new CertEntry(entry.Alias, entry.EntryType, X509CertificateLoader.LoadCertificate(der)));
                }
                catch (CryptographicException)
                {
                    // One unreadable certificate in a chain shouldn't hide the rest of the keystore.
                }
            }
        }

        if (entries.Count == 0)
            return ReadOutcome.Failed(jks.Error ?? Loc.Get("Certificate.EmptyKeystore"));

        return new ReadOutcome(true, false, jks.Truncated ? Loc.Get("Certificate.KeystoreTruncated") : null, entries, "JKS");
    }

    private static ReadOutcome ReadPkcs12(byte[] data, string? password)
    {
        try
        {
            // Ephemeral: this tool only examines the file, so the private key (when present)
            // is never persisted to a machine key store or profile-backed key file.
            var collection = X509CertificateLoader.LoadPkcs12Collection(
                data, password, X509KeyStorageFlags.EphemeralKeySet, Pkcs12LoaderLimits.Defaults);
            var entries = collection.Select(c => new CertEntry(FriendlyAlias(c), "PKCS#12", c)).ToList();
            return new ReadOutcome(true, false, null, entries, "PKCS#12");
        }
        catch (CryptographicException)
        {
            return ReadOutcome.Failed(
                password is null ? Loc.Get("Certificate.PasswordRequired") : Loc.Get("Certificate.WrongPassword"),
                needsPassword: true);
        }
    }

    /// <summary>
    /// Everything that isn't a keystore or a PKCS#12: a single DER certificate, a PEM file
    /// (one certificate or a bundle of several), or a PKCS#7 (.p7b/.p7c) certs-only bundle.
    /// Deliberately avoids <c>X509Certificate2Collection.Import</c> (obsolete as of .NET 9 —
    /// SYSLIB0057) in favor of the format-specific non-obsolete APIs: <c>ImportFromPem</c> for
    /// PEM text, <see cref="X509CertificateLoader"/> for a lone DER certificate, and
    /// <see cref="SignedCms"/> to pull the certificate list out of a PKCS#7 SignedData blob.
    /// </summary>
    private static ReadOutcome? TryGenericImport(byte[] data)
    {
        if (LooksLikePem(data))
            return TryImportPem(data);

        try
        {
            var single = X509CertificateLoader.LoadCertificate(data);
            return new ReadOutcome(true, false, null, [new CertEntry(FriendlyAlias(single), "Certificate", single)], "Certificate");
        }
        catch (CryptographicException)
        {
            // Not a lone DER certificate — fall through and try it as a PKCS#7 bundle instead.
        }

        try
        {
            var cms = new SignedCms();
            cms.Decode(data);
            if (cms.Certificates.Count == 0)
                return null;
            var entries = cms.Certificates.Select(c => new CertEntry(FriendlyAlias(c), "PKCS#7", c)).ToList();
            return new ReadOutcome(true, false, null, entries, "PKCS#7");
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static ReadOutcome? TryImportPem(byte[] data)
    {
        try
        {
            var collection = new X509Certificate2Collection();
            collection.ImportFromPem(Encoding.ASCII.GetString(data));
            if (collection.Count == 0)
                return null;
            var entries = collection.Select(c => new CertEntry(FriendlyAlias(c), "Certificate", c)).ToList();
            return new ReadOutcome(true, false, null, entries, "Certificate");
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>PEM is ASCII text starting with a <c>-----BEGIN</c> armor line — a cheap,
    /// allocation-light way to route text-vs-binary before trying either parser.</summary>
    private static bool LooksLikePem(byte[] data)
    {
        int probe = Math.Min(data.Length, 64);
        for (int i = 0; i < probe; i++)
        {
            byte b = data[i];
            bool isAsciiText = b is (>= 0x09 and <= 0x0D) or (>= 0x20 and <= 0x7E);
            if (!isAsciiText)
                return false;
        }
        return Encoding.ASCII.GetString(data, 0, probe).Contains("-----BEGIN", StringComparison.Ordinal);
    }

    private static string? FriendlyAlias(X509Certificate2 cert)
    {
        var name = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
