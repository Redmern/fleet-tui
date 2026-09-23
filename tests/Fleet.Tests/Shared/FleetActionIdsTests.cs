using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Shared;

public class FleetActionIdsTests
{
    [Theory]
    [InlineData(FleetAction.AddRepository, "add-repository")]
    [InlineData(FleetAction.EditKeybinds, "keybinds")]
    [InlineData(FleetAction.OpenProject, "open-project")]
    [InlineData(FleetAction.NewProject, "new-project")]
    [InlineData(FleetAction.EditFleetConfig, "edit-fleet-config")]
    [InlineData(FleetAction.OpenSettings, "settings-menu")]
    [InlineData(FleetAction.EditAidlcMode, "aidlc-mode")]
    public void An_action_round_trips_through_its_id(FleetAction action, string id)
    {
        Assert.Equal(id, FleetActionIds.For(action));
        Assert.Equal(action, FleetActionIds.Parse(id));
    }

    [Fact]
    public void Parsing_is_case_and_whitespace_tolerant()
    {
        Assert.Equal(FleetAction.AddRepository, FleetActionIds.Parse("  Add-Repository "));
    }

    [Fact]
    public void An_unknown_id_parses_to_none_rather_than_throwing()
    {
        Assert.Equal(FleetAction.None, FleetActionIds.Parse("not-a-real-action"));
        Assert.Equal(FleetAction.None, FleetActionIds.Parse(string.Empty));
    }

    [Fact]
    public void Ids_never_contain_characters_that_would_break_the_generated_lua()
    {
        foreach (var action in Enum.GetValues<FleetAction>())
        {
            var id = FleetActionIds.For(action);

            Assert.DoesNotContain("'", id);
            Assert.DoesNotContain("\\", id);
            Assert.DoesNotContain(" ", id);
        }
    }
}
