namespace SwiftX.Services;

/// <summary>
/// Replaces a file's whole contents in one step, so a reader ever sees the old bytes or the new
/// ones and never a partial write.
/// <para>
/// <b>The scratch file's name is the point.</b> Both stores wrote through a temp file already —
/// <c>orbit.json.tmp</c> and <c>secure-values.json.tmp</c> — but the name was a constant, which
/// makes the temp file shared mutable state between writers rather than private scratch space.
/// Two saves overlapping on it interleave like this:
/// </para>
/// <list type="number">
/// <item>A writes its JSON to the temp file.</item>
/// <item>B truncates the same temp file and starts writing its own, longer, JSON.</item>
/// <item>A moves the temp file over the real one — publishing <em>B's half-written bytes</em>
/// under A's name.</item>
/// </list>
/// <para>
/// The result is a configuration file, or a DPAPI secrets file, that is valid JSON for neither
/// save. <see cref="OrbitStore.Load"/> answers a corrupt <c>orbit.json</c> with a default
/// configuration, so the user's orbit is silently empty at the next start;
/// <see cref="SecureValueStore.Load"/> answers a corrupt blob with an empty list, so every stored
/// secret is gone, with no backup and no undo. The faster race — both writers in
/// <c>File.WriteAllText</c> on one path at once — is merely an <see cref="IOException"/>, which is
/// the benign outcome of the two.
/// </para>
/// <para>
/// A unique scratch name per write removes the interleave; the callers' own lock serializes the
/// publish so the last writer's content is the one that lands. Neither is sufficient alone: the
/// lock is per process, so the unique name is also what keeps a second SwiftX (a developer's build
/// pointed at the same <c>SWIFTX_DATA_DIR</c>) from corrupting the first's file rather than merely
/// losing the race to it.
/// </para>
/// </summary>
internal static class AtomicWrite
{
    /// <summary>
    /// Writes <paramref name="contents"/> and moves it over <paramref name="path"/>, creating the
    /// directory if needed. Throws what the underlying file operations throw — callers decide
    /// whether a failed save is reported or swallowed.
    /// </summary>
    public static void Publish(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Beside the target rather than in %TEMP%: File.Move is only atomic within one volume, and
        // the data directory can be on a different one from the temp directory.
        var scratch = $"{path}.{Environment.ProcessId:x}-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(scratch, contents);
            File.Move(scratch, path, overwrite: true);
        }
        catch
        {
            // A scratch file left behind by a failed write is litter in the user's AppData that
            // nothing will ever clean up, and it is named so that no later run can reuse it.
            TryDelete(scratch);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Diag.Log($"AtomicWrite: could not remove scratch file '{path}': {ex.Message}");
        }
    }
}
