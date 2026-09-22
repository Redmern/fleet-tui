using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Shared;

public class KeymapGroupsTests
{
    [Fact]
    public void Every_configurable_action_appears_in_exactly_one_group()
    {
        var flattened = KeymapGroups.All.SelectMany(g => g.Actions).ToList();

        Assert.Equal(flattened.Count, flattened.Distinct().Count());
        Assert.Equal(flattened, KeymapDefaults.Configurable);
    }

    [Fact]
    public void No_group_is_empty_or_unlabelled()
    {
        Assert.All(KeymapGroups.All, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Label));
            Assert.NotEmpty(g.Actions);
        });
    }

    [Theory]
    [InlineData(FleetAction.QuitFleet, "fleet menu")]
    [InlineData(FleetAction.OpenSettings, "fleet menu")]
    [InlineData(FleetAction.ViewLogs, "fleet menu > settings")]
    [InlineData(FleetAction.EditSettings, "fleet menu > settings")]
    [InlineData(FleetAction.EditFleetConfig, "fleet menu > settings")]
    [InlineData(FleetAction.NewProject, "project picker")]
    [InlineData(FleetAction.MoveDown, "navigation")]
    public void An_action_lives_in_the_expected_group(FleetAction action, string label)
    {
        var group = KeymapGroups.All.Single(g => g.Actions.Contains(action));

        Assert.Equal(label, group.Label);
    }
}
