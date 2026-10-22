using SwiftX.Interop;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// A mouse-only summon alongside the keyboard shortcut and the hot corner: raises
/// <see cref="Triggered"/> once the configured button has been held down continuously for
/// <see cref="LongPressMilliseconds"/>, anywhere on the desktop.
/// <para>
/// Polled rather than hooked, for the same reason <see cref="HotCornerWatcher"/> is: a
/// <c>WH_MOUSE_LL</c> hook sees every click and move system-wide, which is a lot of machinery for
/// a feature that only ever asks "is this one button still down?". A 50&#160;ms <c>WM_TIMER</c> on
/// the same hidden <see cref="MessageWindow"/> the hotkeys and the hot corner already share answers
/// exactly that, on the UI thread, for effectively nothing.
/// </para>
/// <para>
/// Re-arms only once the button is released: holding past the threshold fires <see cref="Triggered"/>
/// once, not once per poll for as long as the button stays down.
/// </para>
/// </summary>
public sealed class MouseTriggerWatcher : IDisposable
{
    /// <summary>How long the button has to stay down before the orbit is summoned.</summary>
    public const int LongPressMilliseconds = 500;

    /// <summary>How often the button state is sampled. Well below the threshold, so the delay the
    /// user feels is the press itself and not the sampling.</summary>
    private const uint PollIntervalMilliseconds = 50;

    // Any non-zero id, unique among the timers on the message window (distinct from HotCornerWatcher's).
    private const nuint TimerId = 0x53774332; // 'SwC2'

    private readonly MessageWindow _window;
    private MouseTriggerButton _button = MouseTriggerButton.Middle;
    private bool _running;
    private bool _disposed;

    /// <summary>False between firing and the button being released — see the class remarks.</summary>
    private bool _armed = true;

    /// <summary><c>Environment.TickCount64</c> when the button went down; null while it is up.</summary>
    private long? _pressedAt;

    /// <summary>Raised on the UI thread once the configured button has been held long enough.</summary>
    public event Action? Triggered;

    public MouseTriggerWatcher(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessage;
    }

    /// <summary>Whether the button is currently being polled.</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Points the watcher at <paramref name="button"/> and starts or stops polling. Safe to call
    /// repeatedly with the same values; any press in progress is discarded, so a button changed
    /// while the old one happens to be held does not fire on the strength of time spent there
    /// before the change.
    /// </summary>
    public void Configure(bool enabled, MouseTriggerButton button)
    {
        _button = button;
        _pressedAt = null;
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
            Diag.Log("MouseTrigger: no message window, button polling not started");
            return;
        }
        if (NativeMethods.SetTimer(_window.Handle, TimerId, PollIntervalMilliseconds, nint.Zero) == 0)
        {
            Diag.Log("MouseTrigger: SetTimer failed, button polling not started");
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
            Diag.Log("MouseTrigger: polling stopped after a failure: " + ex);
        }
    }

    private void Poll()
    {
        if (!IsDown(VirtualKeyFor(_button)))
        {
            _pressedAt = null;
            _armed = true;
            return;
        }

        if (!_armed)
            return;

        long now = Environment.TickCount64;
        if (_pressedAt is not long since)
        {
            _pressedAt = now;
            return;
        }
        if (now - since < LongPressMilliseconds)
            return;

        _armed = false;
        Triggered?.Invoke();
    }

    private static int VirtualKeyFor(MouseTriggerButton button) => button switch
    {
        MouseTriggerButton.Left => NativeMethods.VK_LBUTTON,
        MouseTriggerButton.Right => NativeMethods.VK_RBUTTON,
        MouseTriggerButton.Middle => NativeMethods.VK_MBUTTON,
        MouseTriggerButton.X1 => NativeMethods.VK_XBUTTON1,
        MouseTriggerButton.X2 => NativeMethods.VK_XBUTTON2,
        _ => NativeMethods.VK_MBUTTON,
    };

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
