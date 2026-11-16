using Fleet.Cli.Commands;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;

namespace Fleet.Tests.Cli;

public class MenuRemoteViewTests
{
    public static TheoryData<FleetAction> Owned { get; } = [.. ViewerScreens.Owned];

    [Theory]
    [MemberData(nameof(Owned))]
    public void A_menu_offers_every_owned_screen_back_to_its_viewer_under_its_id(FleetAction action) =>
        Assert.Equal(FleetActionIds.For(action), MenuCommand.HandBackId(action));

    [Theory]
    [InlineData(FleetAction.QuitFleet)]
    [InlineData(FleetAction.ListAgents)]
    [InlineData(FleetAction.EditSettings)]
    [InlineData(FleetAction.SaveSession)]
    [InlineData(FleetAction.None)]
    public void A_menu_never_offers_a_screen_it_does_not_own_back(FleetAction action) =>
        Assert.Null(MenuCommand.HandBackId(action));

    [Theory]
    [InlineData(FleetAction.SwitchProject)]
    [InlineData(FleetAction.Notifications)]
    [InlineData(FleetAction.Remotes)]
    public void An_owned_screen_asked_for_a_view_of_a_remote_runs_on_this_machine(FleetAction action) =>
        Assert.Equal(action, MenuCommand.ScreenForRemoteView("@far", action));

    [Theory]
    [MemberData(nameof(Owned))]
    public void Every_owned_screen_has_a_screen_on_this_machine_for_a_view_of_a_remote(FleetAction action) =>
        Assert.Equal(action, MenuCommand.ScreenForRemoteView("@far", action));

    [Theory]
    [InlineData(FleetAction.None)]
    [InlineData(FleetAction.QuitFleet)]
    [InlineData(FleetAction.OpenProject)]
    [InlineData(FleetAction.ListAgents)]
    public void Any_other_action_for_a_view_of_a_remote_runs_no_screen_here(FleetAction action) =>
        Assert.Equal(FleetAction.None, MenuCommand.ScreenForRemoteView("@far", action));

    [Theory]
    [InlineData("homelab")]
    [InlineData("")]
    [InlineData(null)]
    public void A_project_that_is_not_a_view_of_a_remote_runs_no_screen_here(string? project) =>
        Assert.Equal(FleetAction.None, MenuCommand.ScreenForRemoteView(project, FleetAction.Remotes));

    [Fact]
    public void An_owned_screen_with_no_screen_on_this_machine_closes_instead_of_opening_another() =>
        Assert.Equal(FleetAction.Close, MenuCommand.ScreenForRemoteView("@far", FleetAction.Remotes, [FleetAction.SwitchProject]));
}
