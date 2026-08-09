using System.Text.Json;
using System.Text.Json.Serialization;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>Loads and saves configuration as JSON under %AppData%\SwiftX\ring.json.</summary>
public static class RingStore
{
    public static string DataDirectory { get; } = ResolveDataDirectory();

    private static string ResolveDataDirectory()
    {
        var custom = Environment.GetEnvironmentVariable("SWIFTX_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try { return Path.GetFullPath(custom); }
            catch { /* fall through */ }
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwiftX");
    }

    private static readonly string FilePath = Path.Combine(DataDirectory, "ring.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static bool Exists => File.Exists(FilePath);

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg is not null)
                    return cfg.EnsurePrimary();
            }
        }
        catch (Exception ex)
        {
            Diag.Log("RingStore.Load failed: " + ex.Message);
        }
        return new AppConfig().EnsurePrimary();
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(config, Options);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diag.Log("RingStore.Save failed: " + ex.Message);
        }
    }

    public static bool ExportTo(AppConfig config, string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
            return true;
        }
        catch (Exception ex)
        {
            Diag.Log($"RingStore.ExportTo('{path}') failed: {ex.Message}");
            return false;
        }
    }

    public static AppConfig? ImportFrom(string path)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options);
            if (config is null)
                return null;
            if (config.Rings.Count == 0)
                return null;
            return config.EnsurePrimary();
        }
        catch (Exception ex)
        {
            Diag.Log($"RingStore.ImportFrom('{path}') failed: {ex.Message}");
            return null;
        }
    }
}
