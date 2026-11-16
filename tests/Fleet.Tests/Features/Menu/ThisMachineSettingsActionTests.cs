using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Menu;

public class ThisMachineSettingsActionTests
{
    [Fact]
    public void The_action_round_trips_through_its_id()
    {
        Assert.Equal("this-machine-settings", FleetActionIds.For(FleetAction.ThisMachineSettings));
        Assert.Equal(FleetAction.ThisMachineSettings, FleetActionIds.Parse("this-machine-settings"));
    }

    [Fact]
    public void The_action_is_the_last_one_so_no_stored_number_moves() =>
        Assert.Equal(FleetAction.ThisMachineSettings, Enum.GetValues<FleetAction>().Max());

    [Fact]
    public void The_action_has_no_default_key_and_is_not_offered_for_binding()
    {
        Assert.False(KeymapDefaults.Bindings.ContainsKey(FleetAction.ThisMachineSettings));
        Assert.DoesNotContain(FleetAction.ThisMachineSettings, KeymapDefaults.Configurable);
    }

    [Fact]
    public void Its_row_reads_as_settings_and_opens_a_further_screen()
    {
        Assert.Equal("Settings", FleetMenus.Label(FleetAction.ThisMachineSettings));
        Assert.True(FleetMenus.OpensMore(FleetAction.ThisMachineSettings));
        Assert.False(FleetMenus.IsToggle(FleetAction.ThisMachineSettings));
        Assert.Equal(FleetIcons.For(FleetAction.OpenSettings), FleetIcons.For(FleetAction.ThisMachineSettings));
        Assert.Equal("fleet menu › this machine", FleetMenus.Title(FleetAction.ThisMachineSettings));
    }
}
