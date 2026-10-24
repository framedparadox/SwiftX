using SwiftX.Interop;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// A mouse-only summon alongside the keyboard shortcut and the hot corner: raises
/// <see cref="Triggered"/> once the configured button has either been held down continuously for
/// <see cref="LongPressMilliseconds"/>, been double-clicked, or simply pressed, anywhere on the
/// desktop — whichever gesture <see cref="Models.MouseTriggerAction"/> is currently configured for.
/// <para>
/// Polled rather than hooked, for the same reason <see cref="HotCornerWatcher"/> is: a
/// <c>WH_MOUSE_LL</c> hook sees every click and move system-wide, which is a lot of machinery for
/// a feature that only ever asks "is this one button still down?" (or, for the double-click and
/// none gestures, "did it just go down?"). A 50&#160;ms <c>WM_TIMER</c> on the same hidden
/// <see cref="MessageWindow"/> the hotkeys and the hot corner already share answers exactly that,
/// on the UI thread, for effectively nothing.
/// </para>
/// <para>
/// Re-arms only once the button is released: holding past the long-press threshold, completing
/// a double-click, or the press itself for the none gesture, fires <see cref="Triggered"/> once,
/// not once per poll for as long as the button stays down or gets clicked again.
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
    private MouseTriggerAction _action = MouseTriggerAction.LongPress;
    private bool _running;
    private bool _disposed;

    /// <summary>False between firing and the button being released — see the class remarks.</summary>
    private bool _armed = true;

    /// <summary><c>Environment.TickCount64</c> when the button went down; null while it is up.
    /// Used by the long-press gesture for how long the current press has lasted, and by the
    /// double-click gesture for when the previous press in a possible pair happened.</summary>
    private long? _pressedAt;

    /// <summary>The button's state as of the last poll, for the double-click gesture to notice a
    /// down or up transition against — <see cref="NativeMethods.GetAsyncKeyState"/> reports a
    /// level, not an edge.</summary>
    private bool _wasDown;

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
    /// Points the watcher at <paramref name="button"/> and <paramref name="action"/>, and starts or
    /// stops polling. Safe to call repeatedly with the same values; any press (or first half of a
    /// double-click) in progress is discarded, so a button or gesture changed mid-press does not
    /// fire on the strength of time spent there before the change.
    /// </summary>
    public void Configure(bool enabled, MouseTriggerButton button, MouseTriggerAction action)
    {
        _button = button;
        _action = action;
        _pressedAt = null;
        _armed = true;
        _wasDown = false;

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
        switch (PollKindFor(_action))
        {
            case MouseTriggerPollKind.DoubleClick:
                PollDoubleClick();
                break;
            case MouseTriggerPollKind.None:
                PollNone();
                break;
            default:
                PollLongPress();
                break;
        }
    }

    /// <summary>
    /// Which <c>Poll*</c> method <see cref="Poll"/> calls for <paramref name="action"/> — pulled out
    /// as its own pure mapping, separate from <see cref="Poll"/> itself, so the three-way dispatch
    /// (and that an unrecognized value falls back to the long-press arm specifically, rather than
    /// silently reaching double-click or none) can be exercised in a test without needing to drive
    /// <see cref="NativeMethods.GetAsyncKeyState"/>.
    /// </summary>
    internal static MouseTriggerPollKind PollKindFor(MouseTriggerAction action) => action switch
    {
        MouseTriggerAction.DoubleClick => MouseTriggerPollKind.DoubleClick,
        MouseTriggerAction.None => MouseTriggerPollKind.None,
        _ => MouseTriggerPollKind.LongPress,
    };

    private void PollLongPress()
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

    /// <summary>
    /// The double-click half of <see cref="Poll"/>. Tracks down/up transitions of the configured
    /// button rather than its level (<see cref="PollLongPress"/>'s "is it still down" is irrelevant
    /// here) and hands each press-to-press gap to <see cref="MouseDoubleClickDetector"/> — the
    /// comparison that is actually worth unit-testing — rather than doing the arithmetic inline.
    /// </summary>
    private void PollDoubleClick()
    {
        bool down = IsDown(VirtualKeyFor(_button));
        if (down == _wasDown)
            return;
        _wasDown = down;

        if (!down)
        {
            // The release that follows a trigger is what lets the next press be seen as the
            // start of a fresh pair, rather than another attempt at the gesture that already fired.
            if (!_armed)
            {
                _armed = true;
                _pressedAt = null;
            }
            return;
        }

        if (!_armed)
            return;

        long now = Environment.TickCount64;
        if (_pressedAt is long since &&
            MouseDoubleClickDetector.IsDoubleClick(since, now, (int)NativeMethods.GetDoubleClickTime()))
        {
            _pressedAt = null;
            _armed = false;
            Triggered?.Invoke();
            return;
        }

        // Either the first press of a new pair, or a press that arrived too late to pair with the
        // one before it — either way, it becomes the new "first press" to measure the next one against.
        _pressedAt = now;
    }

    /// <summary>
    /// The "no gesture" half of <see cref="Poll"/>: the button going from up to down while armed is
    /// itself the whole gesture, so <see cref="Triggered"/> fires on that edge rather than after any
    /// hold duration. Tracks the down/up transition the same way <see cref="PollDoubleClick"/> does
    /// — <see cref="IsDown"/> reports a level, not an edge, and a level check here would re-fire on
    /// every poll for as long as the button stayed down — but keeps <see cref="PollLongPress"/>'s
    /// re-arm-on-release discipline: once fired, nothing happens again until the button comes back up.
    /// </summary>
    private void PollNone()
    {
        bool down = IsDown(VirtualKeyFor(_button));
        if (down == _wasDown)
            return;
        _wasDown = down;

        if (!down)
        {
            _armed = true;
            return;
        }

        if (!_armed)
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

/// <summary>Which private <c>Poll*</c> method <see cref="MouseTriggerWatcher.Poll"/> should run —
/// see <see cref="MouseTriggerWatcher.PollKindFor"/>.</summary>
internal enum MouseTriggerPollKind
{
    LongPress,
    DoubleClick,
    None,
}
