using System.Text.Json;
using System.Text.Json.Serialization;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>Loads and saves configuration as JSON under %AppData%\SwiftX\orbit.json.</summary>
public static class OrbitStore
{
    public static string DataDirectory { get; } = ResolveDataDirectory(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// Resolves a per-user SwiftX data directory under <paramref name="folder"/>, honoring
    /// <c>SWIFTX_DATA_DIR</c> first — shared with <see cref="SecureValueStore"/>, which resolves
    /// under <see cref="Environment.SpecialFolder.LocalApplicationData"/> instead of this class's
    /// own <see cref="Environment.SpecialFolder.ApplicationData"/>, so both stores still land in
    /// the same throwaway directory under <c>SWIFTX_DATA_DIR</c> during tests (see
    /// <c>TestEnvironment</c>'s module initializer) even though they diverge in production.
    /// </summary>
    public static string ResolveDataDirectory(Environment.SpecialFolder folder)
    {
        var custom = Environment.GetEnvironmentVariable("SWIFTX_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try { return Path.GetFullPath(custom); }
            catch { /* fall through */ }
        }
        return Path.Combine(Environment.GetFolderPath(folder), "SwiftX");
    }

    private static readonly string FilePath = Path.Combine(DataDirectory, "orbit.json");

    /// <summary>
    /// The file a build from before the ring-to-orbit rename wrote. Read only when
    /// <see cref="FilePath"/> is absent, and never written back to — the first
    /// <see cref="Save"/> after an upgrade lands on <see cref="FilePath"/> and this one is left
    /// alone from then on, so a user who downgrades still has the configuration they had.
    /// </summary>
    private static readonly string LegacyFilePath =
        Path.Combine(DataDirectory, LegacyConfigFormat.LegacyFileName);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static bool Exists => File.Exists(FilePath) || File.Exists(LegacyFilePath);

    public static AppConfig Load()
    {
        try
        {
            // Today's file wins outright; the pre-rename one is a fallback for the single upgrade
            // run that has not written orbit.json yet.
            var path = File.Exists(FilePath) ? FilePath
                     : File.Exists(LegacyFilePath) ? LegacyFilePath
                     : null;
            if (path is not null)
            {
                if (path == LegacyFilePath)
                    Diag.Log($"OrbitStore.Load: upgrading from {LegacyConfigFormat.LegacyFileName}");
                var json = LegacyConfigFormat.Upgrade(File.ReadAllText(path));
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg is not null)
                    return cfg.EnsurePrimary();
            }
        }
        catch (Exception ex)
        {
            Diag.Log("OrbitStore.Load failed: " + ex.Message);
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
            Diag.Log("OrbitStore.Save failed: " + ex.Message);
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
            Diag.Log($"OrbitStore.ExportTo('{path}') failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads a configuration the user picked from anywhere on disk, or null — "keep what you
    /// had" — for anything that isn't one.
    /// <para>
    /// The read is bounded by <see cref="InputLimits.MaxConfigBytes"/> from the file's metadata
    /// before a byte is loaded. Import accepts any path a picker returns, so the guard is not
    /// against a large configuration (a configuration is kilobytes) but against a file that is
    /// not a configuration at all: <c>File.ReadAllText</c> on a mis-picked disk image is an
    /// <see cref="OutOfMemoryException"/> thrown out of a settings command, which is precisely
    /// the failure §22 says must never happen.
    /// </para>
    /// </summary>
    public static AppConfig? ImportFrom(string path)
    {
        try
        {
            var read = InputLimits.ReadText(path, InputLimits.MaxConfigBytes);
            if (!read.Ok)
            {
                Diag.Log($"OrbitStore.ImportFrom('{path}') refused: {read.Error}");
                return null;
            }

            // Same forward-mapping as Load: a configuration exported by a pre-rename build is a
            // file the user can still pick, and it has to import as what it was rather than as
            // nothing.
            var json = LegacyConfigFormat.Upgrade(read.Text);
            var config = JsonSerializer.Deserialize<AppConfig>(json, Options);
            if (config is null)
                return null;
            if (config.Orbits.Count == 0)
                return null;
            return config.EnsurePrimary();
        }
        catch (Exception ex)
        {
            Diag.Log($"OrbitStore.ImportFrom('{path}') failed: {ex.Message}");
            return null;
        }
    }
}
