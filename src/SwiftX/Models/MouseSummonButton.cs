namespace SwiftX.Models;

/// <summary>
/// Which mouse button a long-press summons the orbit with. Persisted by name (see
/// <see cref="AppConfig.MouseSummonButton"/>), so members may be added but never renamed.
/// <see cref="Middle"/> is first on purpose: it is the default, and it is also what
/// <see cref="Services.TolerantEnumConverter{T}"/> falls back to for an unknown name.
/// </summary>
public enum MouseSummonButton
{
    /// <summary>The middle (wheel) button — the default, because left and right already mean click
    /// and context menu almost everywhere.</summary>
    Middle,

    Left,
    Right,
}

/// <summary>Defaults shared by <see cref="AppConfig"/> and the mouse-summon watcher.</summary>
public static class MouseSummonDefaults
{
    /// <summary>How long the button has to stay down before the orbit is summoned.</summary>
    public const int HoldMilliseconds = 400;
}
