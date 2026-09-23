using SwiftX.Interop;

namespace SwiftX.Services;

/// <summary>
/// The one-way channel a second SwiftX process uses to tell the one already running to come
/// forward, before quietly exiting.
/// <para>
/// SwiftX has no taskbar button of its own — the orbit is a borderless
/// <see cref="NativeMethods.WS_EX_TOOLWINDOW"/> popup and everything else lives in the tray — so
/// before this existed, launching the app from Start, the taskbar or a desktop shortcut while it
/// was already running did something genuinely indistinguishable from nothing: the new process saw
/// the single-instance mutex, wrote a line to the log and exited. A user with no window to look at
/// and no icon to click has no way to tell a running SwiftX from a broken one, and the usual
/// reaction is to launch it again. Sending the running instance to Settings makes the app icon
/// always do something visible, which is the whole point.
/// </para>
/// <para>
/// <b>Why a broadcast.</b> The obvious route — find the other instance's window and post to it —
/// is closed off by <see cref="MessageWindow"/>'s own design: its window class name carries the
/// owning process id so a stale registration can never collide, which also means a second process
/// has no class name to hand <c>FindWindow</c>. <c>PostMessage</c> to
/// <see cref="NativeMethods.HWND_BROADCAST"/> reaches every top-level window in the session
/// instead, and the hidden sink is a real (never-shown) top-level popup rather than an
/// <c>HWND_MESSAGE</c> child, so it is among them. The id comes from
/// <c>RegisterWindowMessage</c>, which hands out a value in the system-wide string atom range that
/// no other window can already be using for something of its own, so broadcasting is safe.
/// </para>
/// <para>
/// <b>Scoping.</b> The registered name carries the same data-directory hash the instance mutex
/// does (<see cref="InstanceKey"/>). A developer running SwiftX out of the repo with
/// <c>SWIFTX_DATA_DIR</c> pointed elsewhere is deliberately a separate instance, and it would be
/// surprising if launching one of them summoned the other's Settings.
/// </para>
/// </summary>
public static class SingleInstanceSignal
{
    /// <summary>
    /// Identifies "this installation" for both the single-instance mutex and the window message —
    /// a short hash of the data directory, so two SwiftX copies pointed at different directories
    /// are independent instances that neither block nor signal each other.
    /// </summary>
    public static string InstanceKey { get; } = ComputeInstanceKey();

    /// <summary>The named mutex a second process finds already taken. Session-local (<c>Local\</c>):
    /// two users signed in at once each get their own SwiftX.</summary>
    public static string MutexName { get; } = @"Local\SwiftX.SingleInstance.v1." + InstanceKey;

    /// <summary>
    /// The registered message id meaning "show your Settings window". Zero if registration failed,
    /// in which case the feature degrades to what SwiftX did before: the second instance simply
    /// exits.
    /// </summary>
    public static uint ShowSettingsMessage { get; } = RegisterMessage();

    /// <summary>
    /// Subscribes <paramref name="onShowSettings"/> to the signal, on the UI thread.
    /// <para>
    /// Handlers on <see cref="MessageWindow.MessageReceived"/> see every message the sink
    /// receives, so the id check here is not a formality — a broadcast message arrives alongside
    /// the tray callbacks and <c>WM_TIMER</c> ticks that share the same window.
    /// </para>
    /// </summary>
    public static void Listen(MessageWindow window, Action onShowSettings)
    {
        if (ShowSettingsMessage == 0)
            return;

        window.MessageReceived += (msg, _, _) =>
        {
            if (msg == ShowSettingsMessage)
                onShowSettings();
        };
    }

    /// <summary>
    /// Asks the running instance to surface its Settings window. Returns false if the request
    /// could not even be posted, which callers treat as "nothing more to do" rather than an error
    /// worth showing the user.
    /// </summary>
    /// <remarks>
    /// The <c>AllowSetForegroundWindow</c> call is what makes the difference between Settings
    /// appearing in front of the user and Settings appearing behind whatever they were doing.
    /// Windows grants the right to take the foreground to the process it has just launched on the
    /// user's behalf — this one — and denies it to the long-running tray process that is going to
    /// do the actual showing. Handing the right over before posting transfers it to whoever acts
    /// on the message. It has to happen before the post, because the grant lasts only until this
    /// process next loses its claim.
    /// <para>
    /// Two things this deliberately does not do: it does not wait for an acknowledgement (the
    /// running instance may still be mid-startup and not yet listening, and blocking a process
    /// that is about to exit to find out is worse than the rare missed summon), and it does not
    /// attempt anything if the two processes run at different integrity levels, where UIPI would
    /// drop the broadcast anyway.
    /// </para>
    /// </remarks>
    public static bool RequestOpenSettings()
    {
        if (ShowSettingsMessage == 0)
            return false;

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        bool posted = NativeMethods.PostMessage(
            NativeMethods.HWND_BROADCAST, ShowSettingsMessage, nint.Zero, nint.Zero);
        Diag.Log(posted
            ? "SingleInstanceSignal: asked the running instance to open Settings."
            : "SingleInstanceSignal: broadcast failed; the running instance stays where it is.");
        return posted;
    }

    private static uint RegisterMessage()
    {
        try
        {
            uint id = NativeMethods.RegisterWindowMessage("SwiftX.ShowSettings.v1." + InstanceKey);
            if (id == 0)
                Diag.Log("SingleInstanceSignal: RegisterWindowMessage returned 0.");
            return id;
        }
        catch (Exception ex)
        {
            Diag.Log("SingleInstanceSignal: RegisterWindowMessage failed: " + ex.Message);
            return 0;
        }
    }

    private static string ComputeInstanceKey()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(OrbitStore.DataDirectory.ToLowerInvariant());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes), 0, 8);
    }
}
