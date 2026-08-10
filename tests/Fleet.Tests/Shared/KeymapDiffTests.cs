using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Shared;

public class KeymapDiffTests
{
    [Fact]
    public void A_binding_left_at_its_default_is_not_written()
    {
        var diff = KeymapDiff.AgainstDefaults(
            new Dictionary<FleetAction, string>
            {
                [FleetAction.Refresh] = KeymapDefaults.Bindings[FleetAction.Refresh],
            });

        Assert.Empty(diff);
    }

    [Fact]
    public void A_rebound_action_is_written()
    {
        var diff = KeymapDiff.AgainstDefaults(
            new Dictionary<FleetAction, string> { [FleetAction.Refresh] = "F5" });

        Assert.Equal("F5", diff[FleetAction.Refresh]);
    }

    [Fact]
    public void An_action_fleet_no_longer_has_is_dropped()
    {
        var diff = KeymapDiff.AgainstDefaults(
            new Dictionary<FleetAction, string> { [FleetAction.StopAgent] = "s" });

        Assert.Empty(diff);
    }

    [Fact]
    public void Saving_the_shipped_defaults_writes_nothing_so_later_changes_reach_the_user()
    {
        Assert.Empty(KeymapDiff.AgainstDefaults(KeymapDefaults.Bindings));
    }
}
