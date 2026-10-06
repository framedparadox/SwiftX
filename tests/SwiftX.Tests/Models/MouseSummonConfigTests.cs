using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>Defaults and settings-copy for the mouse long-press summon.</summary>
public class MouseSummonConfigTests
{
    [Fact]
    public void Defaults_are_enabled_middle_button_and_the_shared_hold()
    {
        var config = new AppConfig();

        Assert.True(config.MouseSummonEnabled);
        Assert.Equal(MouseSummonButton.Middle, config.MouseSummonButton);
        Assert.Equal(MouseSummonDefaults.HoldMilliseconds, config.MouseSummonHoldMs);
    }

    [Fact]
    public void CopyAppSettingsFrom_carries_mouse_summon_fields()
    {
        var source = new AppConfig
        {
            MouseSummonEnabled = false,
            MouseSummonButton = MouseSummonButton.Left,
            MouseSummonHoldMs = 900,
        };
        var target = new AppConfig();

        target.CopyAppSettingsFrom(source);

        Assert.False(target.MouseSummonEnabled);
        Assert.Equal(MouseSummonButton.Left, target.MouseSummonButton);
        Assert.Equal(900, target.MouseSummonHoldMs);
    }
}
