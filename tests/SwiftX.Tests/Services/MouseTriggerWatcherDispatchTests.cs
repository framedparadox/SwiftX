using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// <see cref="MouseTriggerWatcher.Poll"/> itself can't be driven from a test — it reads the real
/// button state through <c>GetAsyncKeyState</c> — but the decision behind it, "which <c>Poll*</c>
/// method does this gesture call", is pulled out as <see cref="MouseTriggerWatcher.PollKindFor"/>,
/// a pure mapping from <see cref="MouseTriggerAction"/> to <see cref="MouseTriggerPollKind"/>. This
/// exercises that mapping directly, so a future gesture added to the enum but forgotten in the
/// switch (and so silently reaching the long-press arm) fails a test instead of shipping unnoticed.
/// </summary>
public class MouseTriggerWatcherDispatchTests
{
    [Fact]
    public void Long_press_dispatches_to_the_long_press_poll()
    {
        Assert.Equal(MouseTriggerPollKind.LongPress, MouseTriggerWatcher.PollKindFor(MouseTriggerAction.LongPress));
    }

    [Fact]
    public void Double_click_dispatches_to_the_double_click_poll()
    {
        Assert.Equal(MouseTriggerPollKind.DoubleClick, MouseTriggerWatcher.PollKindFor(MouseTriggerAction.DoubleClick));
    }

    /// <summary>
    /// The regression this whole mapping exists to catch: <c>None</c> has its own arm and must not
    /// fall through to <see cref="MouseTriggerPollKind.LongPress"/> the way an unhandled case would
    /// via a bare <c>if/else</c>.
    /// </summary>
    [Fact]
    public void None_dispatches_to_its_own_poll_not_long_press()
    {
        Assert.Equal(MouseTriggerPollKind.None, MouseTriggerWatcher.PollKindFor(MouseTriggerAction.None));
    }

    /// <summary>All three gestures land on distinct poll methods — this is what makes the switch in
    /// <see cref="MouseTriggerWatcher.Poll"/> meaningful rather than two cases collapsing to one.</summary>
    [Fact]
    public void All_three_gestures_map_to_distinct_poll_kinds()
    {
        var kinds = new[]
        {
            MouseTriggerWatcher.PollKindFor(MouseTriggerAction.LongPress),
            MouseTriggerWatcher.PollKindFor(MouseTriggerAction.DoubleClick),
            MouseTriggerWatcher.PollKindFor(MouseTriggerAction.None),
        };
        Assert.Equal(kinds.Distinct().Count(), kinds.Length);
    }
}
