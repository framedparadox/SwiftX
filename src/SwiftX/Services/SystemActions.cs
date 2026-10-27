using System.Diagnostics;
using System.Threading;
using SwiftX.Interop;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// The built-in system operations <see cref="SystemOrbitItems"/> offers from the "Add to orbit"
/// sidebar's Controls section — Lock, Sleep, Mute, Screenshot, Empty Recycle Bin, Task Manager —
/// plus the one that plays back a user-mapped combination (see <see cref="SendKeyboardAction"/>).
/// Each is a single, well-known Windows action with no configuration of its own; failures are
/// logged rather than surfaced, the same way <see cref="Launcher"/> treats a launch that fails.
/// </summary>
public static class SystemActions
{
    public static void ToggleMute() => SendKey(NativeMethods.VK_VOLUME_MUTE);

    /// <summary>The same full-screen capture Print Screen performs: copies the screen to the
    /// clipboard. No file is written — Snipping Tool or a tool of the user's own owns anything
    /// more than that.</summary>
    public static void CaptureScreenshot() => SendKey(NativeMethods.VK_SNAPSHOT);

    public static void LockWorkstation()
    {
        try
        {
            if (!NativeMethods.LockWorkStation())
                Diag.Log("SystemActions.LockWorkstation: LockWorkStation returned false");
        }
        catch (Exception ex)
        {
            Diag.Log($"SystemActions.LockWorkstation FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void Sleep()
    {
        try
        {
            if (!NativeMethods.SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false))
                Diag.Log("SystemActions.Sleep: SetSuspendState returned false");
        }
        catch (Exception ex)
        {
            Diag.Log($"SystemActions.Sleep FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void EmptyRecycleBin()
    {
        try
        {
            const uint flags = NativeMethods.SHERB_NOCONFIRMATION
                | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND;
            int hr = NativeMethods.SHEmptyRecycleBin(nint.Zero, null, flags);
            // 0 (S_OK) and -2147418113/0x8000FFFF-ish "already empty" codes both mean nothing is
            // left to empty; anything else is worth a log line even though there is no UI here to
            // show it in.
            if (hr != 0 && unchecked((uint)hr) != 0x8000FFFF)
                Diag.Log($"SystemActions.EmptyRecycleBin: SHEmptyRecycleBin returned 0x{hr:X8}");
        }
        catch (Exception ex)
        {
            Diag.Log($"SystemActions.EmptyRecycleBin FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Diag.Log($"SystemActions.OpenTaskManager FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs a <see cref="OrbitItem.Arguments"/>-stored command via <c>cmd.exe /c</c> — what a
    /// <see cref="OrbitItemKind.CommandAction"/> item does on activation. Does nothing (silently,
    /// bar the log line) for an item that was never configured.
    /// </summary>
    public static void RunCommand(OrbitItem item)
    {
        var command = item.Arguments;
        if (string.IsNullOrWhiteSpace(command))
        {
            Diag.Log($"SystemActions.RunCommand: '{item.DisplayName}' has no command (Arguments='{item.Arguments}')");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            Diag.Log($"SystemActions.RunCommand FAILED for '{command}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Plays back a <see cref="OrbitItem.Arguments"/>-stored <see cref="HotkeyGesture"/> as a real
    /// keystroke — what a <see cref="OrbitItemKind.KeyboardAction"/> item does on activation. Does
    /// nothing (silently, bar the log line) for an item that was never configured: that can only
    /// happen to a hand-edited config, since the item is never added without a gesture already on
    /// it — see the sidebar's "Map keyboard action" row.
    /// </summary>
    /// <param name="restoreFocusTo">
    /// The window the combination is actually meant for — whatever had focus before the orbit
    /// itself took it (<see cref="OrbitWindow.SummonedFromHwnd"/>). Without this, the combination
    /// would be injected while the orbit is still the foreground window and so would reach the
    /// orbit, not whatever the user meant it for. Zero skips the restore (nothing was in front, or
    /// the caller has no window to go back to), and the combination is sent as-is.
    /// </param>
    public static void SendKeyboardAction(OrbitItem item, nint restoreFocusTo = 0)
    {
        if (!HotkeyGesture.TryParse(item.Arguments, out var gesture))
        {
            Diag.Log($"SystemActions.SendKeyboardAction: '{item.DisplayName}' has no usable shortcut (Arguments='{item.Arguments}')");
            return;
        }

        try
        {
            if (restoreFocusTo != 0 && NativeMethods.GetForegroundWindow() != restoreFocusTo)
            {
                WindowChrome.ForceForeground(restoreFocusTo);
                // The orbit's own dismiss animation is already under way by the time this runs
                // (see OrbitWindow.OnSelectAsync) and this thread has nothing better to do while
                // the focus change lands, so a short blocking wait is simplest — the same tradeoff
                // Launcher.LaunchCore makes for a local target on the calling thread.
                Thread.Sleep(30);
            }

            var downs = ModifierKeys(gesture.Modifiers).Append((int)gesture.Key).ToArray();

            var inputs = new NativeMethods.INPUT[downs.Length * 2];
            for (int i = 0; i < downs.Length; i++)
                inputs[i] = KeyInput(downs[i], up: false);
            // Released in reverse order — the key itself first, then modifiers outermost-last —
            // the same order letting go of a physical chord naturally happens in.
            for (int i = 0; i < downs.Length; i++)
                inputs[downs.Length + i] = KeyInput(downs[downs.Length - 1 - i], up: true);

            int size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>();
            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, size);
            if (sent != inputs.Length)
                Diag.Log($"SystemActions.SendKeyboardAction: SendInput sent {sent}/{inputs.Length} events for '{gesture}'");
        }
        catch (Exception ex)
        {
            Diag.Log($"SystemActions.SendKeyboardAction FAILED for '{gesture}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IEnumerable<int> ModifierKeys(HotkeyModifiers modifiers)
    {
        if (modifiers.HasFlag(HotkeyModifiers.Control)) yield return NativeMethods.VK_CONTROL;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) yield return NativeMethods.VK_MENU;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) yield return NativeMethods.VK_SHIFT;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) yield return NativeMethods.VK_LWIN;
    }

    private static void SendKey(int virtualKey)
    {
        try
        {
            var inputs = new[] { KeyInput(virtualKey, up: false), KeyInput(virtualKey, up: true) };
            int size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>();
            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, size);
            if (sent != inputs.Length)
                Diag.Log($"SystemActions.SendKey(0x{virtualKey:X2}): SendInput sent {sent}/{inputs.Length} events");
        }
        catch (Exception ex)
        {
            Diag.Log($"SystemActions.SendKey(0x{virtualKey:X2}) FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static NativeMethods.INPUT KeyInput(int virtualKey, bool up) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        ki = new NativeMethods.KEYBDINPUT
        {
            wVk = (short)virtualKey,
            dwFlags = up ? (int)NativeMethods.KEYEVENTF_KEYUP : 0,
        },
    };
}
