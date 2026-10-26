namespace SwiftX.Models;

/// <summary>
/// Which gesture on the configured button summons the orbit — see Services.MouseTriggerWatcher.
/// <see cref="LongPress"/> has to stay the first declared member and <see cref="DoubleClick"/> the
/// second: an unrecognized or missing persisted value falls back to the first one (see
/// Services.TolerantEnumConverterFactory), so every existing user's config keeps today's behavior
/// untouched. A new gesture is appended after the ones already here, never inserted before them.
/// </summary>
public enum MouseTriggerAction
{
    LongPress,
    DoubleClick,

    /// <summary>No gesture at all: the orbit summons on a plain press of the configured button,
    /// immediately, with no hold or second click required. Lets a button whose everyday
    /// single-click meaning the user wants to reclaim trigger the orbit right away, while
    /// <see cref="LongPress"/> and <see cref="DoubleClick"/> stay available as the gentler options
    /// for a button that is already doing something else on a plain click.</summary>
    None,
}
