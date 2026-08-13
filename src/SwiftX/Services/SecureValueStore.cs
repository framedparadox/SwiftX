using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// Persists key/secret pairs under the SwiftX data directory. The entire file — key names
/// included, not just values — is encrypted as one blob with Windows DPAPI
/// (<see cref="ProtectedData"/>, CurrentUser scope), so opening <c>secure-values.json</c> in a
/// text editor or any other app shows nothing but an opaque base64 string tied to this Windows
/// account. Key names alone (e.g. "AWS_SECRET_KEY", "Prod DB password") are sensitive metadata in
/// their own right; leaving them in plaintext next to an encrypted value would have defeated much
/// of the point.
/// </summary>
public static class SecureValueStore
{
    private static readonly string FilePath = Path.Combine(RingStore.DataDirectory, "secure-values.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int CurrentVersion = 2;

    private sealed class StoredFile
    {
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Base64 DPAPI blob of a JSON-serialized <c>List&lt;PlainEntry&gt;</c> — the
        /// whole record, keys included. Present from <see cref="CurrentVersion"/> on.</summary>
        public string? Protected { get; set; }

        /// <summary>
        /// Version 1 only: a plaintext <see cref="LegacyEntry.Key"/> sitting beside a per-value
        /// DPAPI blob. <see cref="Load"/> reads this shape for one last migration pass and never
        /// writes it again — the first <see cref="Save"/> after loading a v1 file rewrites it as
        /// <see cref="Protected"/> so the plaintext key names don't linger on disk.
        /// </summary>
        public List<LegacyEntry>? Entries { get; set; }
    }

    private sealed class PlainEntry
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    private sealed class LegacyEntry
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public string ProtectedValue { get; set; } = "";
    }

    public static IReadOnlyList<SecureValueEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var json = File.ReadAllText(FilePath);
            var file = JsonSerializer.Deserialize<StoredFile>(json, Options);
            if (file is null)
                return [];

            if (!string.IsNullOrEmpty(file.Protected))
                return LoadCurrent(file.Protected);

            if (file.Entries is not null)
                return LoadLegacyAndUpgrade(file.Entries);

            return [];
        }
        catch (Exception ex)
        {
            Diag.Log("SecureValueStore.Load failed: " + ex.Message);
            return [];
        }
    }

    private static List<SecureValueEntry> LoadCurrent(string protectedBlob)
    {
        var json = Unprotect(protectedBlob);
        if (json.Length == 0)
            return [];

        var entries = JsonSerializer.Deserialize<List<PlainEntry>>(json, Options) ?? [];
        return entries.Select(e => new SecureValueEntry
        {
            Id = string.IsNullOrEmpty(e.Id) ? Guid.NewGuid().ToString("N") : e.Id,
            Key = e.Key,
            Value = e.Value,
        }).ToList();
    }

    /// <summary>Reads the pre-encryption-hardening format, then immediately re-saves in the
    /// current whole-file-encrypted format so the plaintext key names don't survive past this one
    /// read.</summary>
    private static List<SecureValueEntry> LoadLegacyAndUpgrade(List<LegacyEntry> stored)
    {
        var result = stored.Select(e => new SecureValueEntry
        {
            Id = string.IsNullOrEmpty(e.Id) ? Guid.NewGuid().ToString("N") : e.Id,
            Key = e.Key ?? "",
            Value = Unprotect(e.ProtectedValue),
        }).ToList();

        try { Save(result); }
        catch (Exception ex) { Diag.Log("SecureValueStore: legacy upgrade save failed: " + ex.Message); }

        return result;
    }

    public static void Save(IEnumerable<SecureValueEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(RingStore.DataDirectory);
            var plain = entries.Select(e => new PlainEntry { Id = e.Id, Key = e.Key, Value = e.Value }).ToList();
            var innerJson = JsonSerializer.Serialize(plain, Options);
            var file = new StoredFile { Version = CurrentVersion, Protected = Protect(innerJson) };
            var json = JsonSerializer.Serialize(file, Options);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diag.Log("SecureValueStore.Save failed: " + ex.Message);
            throw;
        }
    }

    private static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return "";
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
            return "";
        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            Diag.Log("SecureValueStore.Unprotect failed: " + ex.Message);
            return "";
        }
    }
}
