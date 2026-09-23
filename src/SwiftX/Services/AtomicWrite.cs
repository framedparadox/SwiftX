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
            MoveOverExisting(scratch, path);
        }
        catch
        {
            // A scratch file left behind by a failed write is litter in the user's AppData that
            // nothing will ever clean up, and it is named so that no later run can reuse it.
            TryDelete(scratch);
            throw;
        }
    }

    /// <summary>How many times the replace is re-attempted before the failure is handed to the
    /// caller. Short, because this is riding out somebody else's open handle, not waiting for a
    /// lock that a well-behaved peer would announce.</summary>
    private const int MoveAttempts = 5;

    /// <summary>
    /// The gap between attempts, which doubles each time — 20, 40, 80, 160 ms, so roughly a third
    /// of a second in total. Long enough to outlast a scanner reading the file it was just told
    /// about; short enough that a genuinely locked file still reports promptly rather than hanging
    /// a settings toggle.
    /// </summary>
    private const int MoveRetryDelayMs = 20;

    /// <summary>
    /// <c>File.Move(overwrite: true)</c>, retried past the transient failure that replacing a file
    /// somebody else currently has open produces.
    /// <para>
    /// <b>The destination being readable is not the same as it being replaceable.</b> Windows
    /// implements the overwrite as <c>MoveFileEx</c> with <c>MOVEFILE_REPLACE_EXISTING</c>, which
    /// needs delete access to the file it is replacing — so any other handle open on it without
    /// <c>FILE_SHARE_DELETE</c> fails the move outright. That is the normal state of affairs for
    /// a file in the user's AppData: the app's own <c>Load</c> on another thread, an antivirus
    /// on-access scan, a backup or folder-sync agent, or a second SwiftX pointed at the same
    /// <c>SWIFTX_DATA_DIR</c> all hold one for a few milliseconds at a time.
    /// </para>
    /// <para>
    /// Without a retry that window surfaced as a save that simply did not happen.
    /// <see cref="OrbitStore.Save"/> swallows its exception, so a settings change was silently
    /// discarded and came back at the next start; <see cref="SecureValueStore.Save"/> rethrows, so
    /// Secure values showed the user "Access to the path … is denied" for a secret that was
    /// perfectly savable a moment later. Neither is a permissions problem, which is what both
    /// messages claim — and note the exception type: a sharing violation on the destination of a
    /// replace arrives as <see cref="UnauthorizedAccessException"/>, not
    /// <see cref="IOException"/>, so a caller catching only the latter does not see it.
    /// </para>
    /// <para>
    /// Retrying is safe because the scratch file is private to this call (see the type remarks):
    /// re-attempting the move cannot publish anyone else's bytes, and the file either has been
    /// replaced whole or has not been touched at all. A move that never succeeds throws exactly
    /// what it threw before, so a real permissions failure still reaches the caller — it just
    /// takes a third of a second longer to say so.
    /// </para>
    /// <para>
    /// The wait is synchronous, and both stores are saved from the UI thread, so a contended
    /// publish costs that thread up to ~300 ms. That is deliberate: it is bounded, it only happens
    /// when something really is holding the file, and the alternative on offer — making the whole
    /// <c>Save</c> surface asynchronous — buys nothing for a write that every caller already
    /// treats as instantaneous, while losing the write is what users actually noticed.
    /// </para>
    /// </summary>
    private static void MoveOverExisting(string scratch, string path)
    {
        int delay = MoveRetryDelayMs;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(scratch, path, overwrite: true);
                if (attempt > 1)
                    Diag.Log($"AtomicWrite: published '{path}' on attempt {attempt}.");
                return;
            }
            catch (Exception ex) when (attempt < MoveAttempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(delay);
                delay *= 2;
            }
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
