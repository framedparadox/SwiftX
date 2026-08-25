using Microsoft.UI.Windowing;
using SwiftX.Interop;
using SwiftX.Models;
using Windows.Graphics;

namespace SwiftX.Services;

/// <summary>
/// A macOS-style hot corner: raises <see cref="Triggered"/> once the pointer has rested for a
/// moment in the configured corner of the screen it is on, giving the ring a mouse-only summon
/// alongside the keyboard shortcut and the tray icon.
/// <para>
/// The pointer is polled rather than hooked. A <c>WH_MOUSE_LL</c> hook would see every movement
/// first-hand, but it is a system-wide hook on the input path — a heavy, easily-blamed thing to
/// install for a feature that only ever asks "is the cursor in that corner yet?". A 50 ms
/// <c>WM_TIMER</c> on the hidden <see cref="MessageWindow"/> answers exactly that, on the UI
/// thread, through the message pump the tray icon and hotkeys already use, and costs nothing
/// measurable.
/// </para>
/// <para>
/// Two guards keep it from firing when the corner was not the point: the pointer has to stay in
/// the patch for <see cref="DwellMilliseconds"/> (a corner clipped in passing does not count), and
/// having fired it will not fire again until the pointer has left the patch, so parking the mouse
/// in the corner summons the ring once rather than every tick.
/// </para>
/// </summary>
public sealed class HotCornerWatcher : IDisposable
{
    /// <summary>How long the pointer has to sit in the corner before the ring is summoned.</summary>
    public const int DwellMilliseconds = 250;

    /// <summary>How often the pointer is sampled. Well below the dwell, so the delay the user
    /// feels is the dwell and not the sampling.</summary>
    private const uint PollIntervalMilliseconds = 50;

    // Any non-zero id, unique among the timers on the message window (it owns no others).
    private const nuint TimerId = 0x53774331; // 'SwC1'

    private readonly MessageWindow _window;
    private HotCorner _corner = HotCorner.TopLeft;
    private bool _running;
    private bool _disposed;

    /// <summary>False between firing and the pointer leaving the corner — see the class remarks.</summary>
    private bool _armed = true;

    /// <summary><c>Environment.TickCount64</c> when the pointer entered the corner; null when it
    /// is outside, or while a held mouse button is keeping the dwell from starting.</summary>
    private long? _enteredAt;

    /// <summary>Raised on the UI thread when the pointer has dwelt in the configured corner.</summary>
    public event Action? Triggered;

    public HotCornerWatcher(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessage;
    }

    /// <summary>Whether the pointer is currently being polled.</summary>
    public bool IsRunning => _running;

    /// <summary>The corner being watched.</summary>
    public HotCorner Corner => _corner;

    /// <summary>
    /// Points the watcher at <paramref name="corner"/> and starts or stops polling. Safe to call
    /// repeatedly with the same values; any dwell in progress is discarded, so a corner changed
    /// while the pointer happens to sit in the new one does not fire on the strength of time
    /// spent there before the change.
    /// </summary>
    public void Configure(bool enabled, HotCorner corner)
    {
        _corner = corner;
        _enteredAt = null;
        _armed = true;

        if (enabled)
            Start();
        else
            Stop();
    }

    private void Start()
    {
        if (_running || _disposed)
            return;
        if (_window.Handle == nint.Zero)
        {
            Diag.Log("HotCorner: no message window, pointer polling not started");
            return;
        }
        if (NativeMethods.SetTimer(_window.Handle, TimerId, PollIntervalMilliseconds, nint.Zero) == 0)
        {
            Diag.Log("HotCorner: SetTimer failed, pointer polling not started");
            return;
        }
        _running = true;
    }

    private void Stop()
    {
        if (!_running)
            return;
        _running = false;
        if (_window.Handle != nint.Zero)
            NativeMethods.KillTimer(_window.Handle, TimerId);
    }

    private void OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg != NativeMethods.WM_TIMER || (nuint)wParam != TimerId || !_running)
            return;
        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            // Polling runs 20 times a second, so a fault that repeats would bury the log. Stand
            // the feature down instead and say so once; everything else about the app is fine.
            Stop();
            Diag.Log("HotCorner: polling stopped after a failure: " + ex);
        }
    }

    private void Poll()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
            return;

        // The corner of the display the pointer is on, so each monitor has its own hot corner —
        // OuterBounds, not WorkArea: the corner of the screen is where the pointer stops, taskbar
        // or not.
        var bounds = DisplayArea.GetFromPoint(
            new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).OuterBounds;

        bool inside = HotCornerZone.Contains(
            _corner, cursor.X, cursor.Y, bounds.X, bounds.Y, bounds.Width, bounds.Height);

        if (!inside)
        {
            _enteredAt = null;
            _armed = true;
            return;
        }

        if (!_armed)
            return;

        // A held button means the pointer is dragging something into the corner — a window being
        // thrown at it for Windows' own snap, a selection, a file — not a summon. Keep the dwell
        // unstarted so releasing the button in the corner starts the clock fresh.
        if (IsAnyMouseButtonDown())
        {
            _enteredAt = null;
            return;
        }

        long now = Environment.TickCount64;
        if (_enteredAt is not long since)
        {
            _enteredAt = now;
            return;
        }
        if (now - since < DwellMilliseconds)
            return;

        _armed = false;
        _enteredAt = null;
        Triggered?.Invoke();
    }

    private static bool IsAnyMouseButtonDown() =>
        IsDown(NativeMethods.VK_LBUTTON) || IsDown(NativeMethods.VK_RBUTTON) || IsDown(NativeMethods.VK_MBUTTON);

    private static bool IsDown(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _window.MessageReceived -= OnMessage;
    }
}
