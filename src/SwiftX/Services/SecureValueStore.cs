using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// Persists key/secret pairs under the SwiftX data directory. Values are encrypted with
/// Windows DPAPI (<see cref="ProtectedData"/>, CurrentUser scope) so plaintext never sits on disk.
/// </summary>
public static class SecureValueStore
{
    private static readonly string FilePath = Path.Combine(RingStore.DataDirectory, "secure-values.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class StoredFile
    {
        public int Version { get; set; } = 1;
        public List<StoredEntry> Entries { get; set; } = [];
    }

    private sealed class StoredEntry
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
            if (file?.Entries is null)
                return [];

            var result = new List<SecureValueEntry>();
            foreach (var stored in file.Entries)
            {
                result.Add(new SecureValueEntry
                {
                    Id = string.IsNullOrEmpty(stored.Id) ? Guid.NewGuid().ToString("N") : stored.Id,
                    Key = stored.Key ?? "",
                    Value = Unprotect(stored.ProtectedValue),
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            Diag.Log("SecureValueStore.Load failed: " + ex.Message);
            return [];
        }
    }

    public static void Save(IEnumerable<SecureValueEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(RingStore.DataDirectory);
            var file = new StoredFile
            {
                Entries = entries.Select(e => new StoredEntry
                {
                    Id = e.Id,
                    Key = e.Key,
                    ProtectedValue = Protect(e.Value),
                }).ToList(),
            };
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
