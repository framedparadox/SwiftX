using SwiftX.Interop;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// Summons the orbit when the configured mouse button is held down for a moment — a mouse-only
/// counterpart to the keyboard shortcut and the hot corner, without installing a low-level hook.
/// <para>
/// The button state is polled on a 50 ms <c>WM_TIMER</c> on the hidden <see cref="MessageWindow"/>,
/// the same path <see cref="HotCornerWatcher"/> uses. A <c>WH_MOUSE_LL</c> hook would see every
/// click first-hand, but it is a system-wide input-path hook for a feature that only asks "has this
/// button been held long enough?".
/// </para>
/// <para>
/// Firing once per hold (re-armed on release) keeps a parked middle-button press from toggling the
/// orbit every tick. The default is a long-press of the middle button; the hold length and the
/// button are config so Settings can expose them later without another summon path.
/// </para>
/// </summary>
public sealed class MouseSummonWatcher : IDisposable
{
    /// <summary>How long the button has to stay down before the orbit is summoned.</summary>
    public const int DefaultHoldMilliseconds = MouseSummonDefaults.HoldMilliseconds;

    /// <summary>How often the button is sampled. Well below the hold, so the delay the user feels
    /// is the hold and not the sampling.</summary>
    private const uint PollIntervalMilliseconds = 50;

    // Distinct from HotCornerWatcher's timer id on the same message window.
    private const nuint TimerId = 0x53774D31; // 'SwM1'

    private readonly MessageWindow _window;
    private bool _running;
    private bool _disposed;
    private bool _enabled;
    private MouseSummonButton _button = MouseSummonButton.Middle;
    private int _holdMilliseconds = DefaultHoldMilliseconds;

    /// <summary>False between firing and the button being released — see the class remarks.</summary>
    private bool _armed = true;

    /// <summary><c>Environment.TickCount64</c> when the watched button went down; null when it is
    /// up, or while another mouse button is also held (a chord is not a summon).</summary>
    private long? _pressedAt;

    /// <summary>Raised on the UI thread when the configured button has been held long enough.</summary>
    public event Action? Triggered;

    public MouseSummonWatcher(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessage;
    }

    public bool IsRunning => _running;

    /// <summary>
    /// Applies <paramref name="enabled"/>, <paramref name="button"/> and <paramref name="holdMs"/>
    /// and starts or stops polling. Safe to call repeatedly; any hold in progress is discarded so a
    /// setting change mid-press does not fire on time spent under the old values.
    /// </summary>
    public void Configure(bool enabled, MouseSummonButton button, int holdMs)
    {
        _enabled = enabled;
        _button = button;
        _holdMilliseconds = Math.Clamp(holdMs, 150, 2000);
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
            Diag.Log("MouseSummon: no message window, button polling not started");
            return;
        }
        if (NativeMethods.SetTimer(_window.Handle, TimerId, PollIntervalMilliseconds, nint.Zero) == 0)
        {
            Diag.Log("MouseSummon: SetTimer failed, button polling not started");
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
            Stop();
            Diag.Log("MouseSummon: polling stopped after a failure: " + ex);
        }
    }

    private void Poll()
    {
        if (!_enabled)
            return;

        bool down = IsButtonDown(_button);
        if (!down)
        {
            _pressedAt = null;
            _armed = true;
            return;
        }

        if (!_armed)
            return;

        // A chord (another button held with the summon button) is a drag or a browser-gesture, not
        // a dock summon. Keep the hold unstarted so releasing the extras starts the clock fresh.
        if (IsOtherMouseButtonDown(_button))
        {
            _pressedAt = null;
            return;
        }

        long now = Environment.TickCount64;
        if (_pressedAt is not long since)
        {
            _pressedAt = now;
            return;
        }
        if (now - since < _holdMilliseconds)
            return;

        _armed = false;
        _pressedAt = null;
        Triggered?.Invoke();
    }

    /// <summary>
    /// Pure hold/re-arm state machine used by tests. Returns whether the hold should fire given the
    /// previous armed/pressed state and the latest sample.
    /// </summary>
    internal static bool ShouldFire(
        bool buttonDown, bool otherDown, bool armed, ref long? pressedAt, int holdMs, long now)
    {
        if (!buttonDown)
        {
            pressedAt = null;
            return false;
        }
        if (!armed)
            return false;
        if (otherDown)
        {
            pressedAt = null;
            return false;
        }
        if (pressedAt is not long since)
        {
            pressedAt = now;
            return false;
        }
        return now - since >= holdMs;
    }

    private static bool IsButtonDown(MouseSummonButton button) =>
        IsDown(VirtualKey(button));

    private static bool IsOtherMouseButtonDown(MouseSummonButton button)
    {
        foreach (var candidate in new[]
                 {
                     MouseSummonButton.Left, MouseSummonButton.Right, MouseSummonButton.Middle,
                 })
        {
            if (candidate == button)
                continue;
            if (IsButtonDown(candidate))
                return true;
        }
        return false;
    }

    private static int VirtualKey(MouseSummonButton button) => button switch
    {
        MouseSummonButton.Left => NativeMethods.VK_LBUTTON,
        MouseSummonButton.Right => NativeMethods.VK_RBUTTON,
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
