using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

public class MouseTriggerActionTests
{
    /// <summary>
    /// <see cref="MouseTriggerAction.LongPress"/> has to stay the enum's first declared member:
    /// <c>Services.TolerantEnumConverter{T}</c> falls back to an enum's first member for a config
    /// that predates this setting, and that fallback is what gives every existing user today's
    /// long-press behavior with no migration step. See <see cref="AppConfig.MouseTriggerAction"/>.
    /// </summary>
    [Fact]
    public void LongPress_is_the_default()
    {
        Assert.Equal(MouseTriggerAction.LongPress, default(MouseTriggerAction));
        Assert.Equal(MouseTriggerAction.LongPress, new AppConfig().MouseTriggerAction);
    }

    /// <summary>
    /// <see cref="MouseTriggerAction.None"/> — the "fire on a plain press" gesture — was added after
    /// the original two, not between or before them, for the same reason <see cref="LongPress"/> has
    /// to stay first: a persisted config's ordinal position is never read, but a reordering would
    /// still be the kind of change that invites one, and nothing about adding a third gesture
    /// requires moving the first two.
    /// </summary>
    [Fact]
    public void Members_are_declared_long_press_double_click_none_in_that_order()
    {
        Assert.Equal(
            new[] { MouseTriggerAction.LongPress, MouseTriggerAction.DoubleClick, MouseTriggerAction.None },
            (MouseTriggerAction[])Enum.GetValues(typeof(MouseTriggerAction)));
    }
}
