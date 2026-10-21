using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// The Layout setting (<see cref="AppConfig.Arrangement"/>) as a piece of configuration: what a
/// fresh config gets, and that a backup/restore carries it. Reading an unknown name is covered with
/// the other enums in <c>TolerantEnumConfigTests</c>.
/// </summary>
public class ArrangementConfigTests
{
    [Fact]
    public void A_fresh_config_draws_the_ring()
    {
        // Ring is also what a config written before the setting existed reads back as, since the
        // property is simply absent there.
        Assert.Equal(OrbitArrangement.Ring, new AppConfig().Arrangement);
    }

    [Fact]
    public void CopyAppSettingsFrom_carries_the_arrangement()
    {
        var source = new AppConfig { Arrangement = OrbitArrangement.HexNest };
        var target = new AppConfig();
        target.CopyAppSettingsFrom(source);

        Assert.Equal(OrbitArrangement.HexNest, target.Arrangement);
    }
}
