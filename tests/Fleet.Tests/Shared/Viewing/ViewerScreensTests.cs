using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Tests.Shared.Viewing;

public class ViewerScreensTests
{
    [Fact]
    public void The_screens_the_machine_you_sit_at_owns_are_switch_project_notifications_and_remote_machines()
    {
        Assert.Equal([FleetAction.SwitchProject, FleetAction.Notifications, FleetAction.Remotes], ViewerScreens.Owned);
        Assert.Equal(["switch-project", "notifications", "remotes"], ViewerScreens.Ids);
    }

    [Theory]
    [InlineData("switch-project", true)]
    [InlineData("notifications", true)]
    [InlineData("remotes", true)]
    [InlineData("quit", false)]
    [InlineData("list-agents", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_an_owned_id_is_owned(string? id, bool owned) => Assert.Equal(owned, ViewerScreens.Owns(id));

    [Fact]
    public void A_viewer_that_announced_nothing_takes_back_only_switch_project_and_notifications()
    {
        Assert.Equal(["switch-project", "notifications"], ViewerScreens.TakenBackBy(null));
        Assert.Equal(["switch-project", "notifications"], ViewerScreens.TakenBackBy([]));
    }

    [Fact]
    public void A_viewer_that_announced_ids_takes_back_the_owned_ones_among_them()
    {
        Assert.Equal(["remotes", "switch-project"], ViewerScreens.TakenBackBy(["remotes", "quit", "switch-project", "theme-of-tomorrow"]));
    }

    [Theory]
    [InlineData(FleetAction.Remotes, ScreenRoute.Here)]
    [InlineData(FleetAction.QuitFleet, ScreenRoute.Here)]
    public void A_menu_on_the_machine_you_sit_at_runs_everything_here(FleetAction action, ScreenRoute route) =>
        Assert.Equal(route, ViewerScreens.Route(null, action));

    [Theory]
    [InlineData(FleetAction.Remotes, ScreenRoute.HandBack)]
    [InlineData(FleetAction.SwitchProject, ScreenRoute.HandBack)]
    [InlineData(FleetAction.QuitFleet, ScreenRoute.Here)]
    public void A_menu_drawn_for_a_remote_view_hands_owned_screens_back(FleetAction action, ScreenRoute route) =>
        Assert.Equal(route, ViewerScreens.Route(new ViewedFrom(ViewKind.View, " @home"), action));

    [Theory]
    [InlineData(FleetAction.Remotes, ScreenRoute.Hint)]
    [InlineData(FleetAction.Notifications, ScreenRoute.Hint)]
    [InlineData(FleetAction.ListAgents, ScreenRoute.Here)]
    public void A_menu_drawn_for_an_ssh_window_hints_at_owned_screens(FleetAction action, ScreenRoute route) =>
        Assert.Equal(route, ViewerScreens.Route(new ViewedFrom(ViewKind.Ssh, "laptop"), action));

    [Theory]
    [InlineData(ViewKind.View, " @home", "view: @home")]
    [InlineData(ViewKind.Ssh, "laptop", "ssh:laptop")]
    public void A_viewer_mark_round_trips(ViewKind kind, string name, string mark)
    {
        Assert.Equal(mark, new ViewedFrom(kind, name).Mark);
        Assert.Equal(new ViewedFrom(kind, name), ViewedFrom.Parse(mark));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("view:")]
    [InlineData("tunnel:laptop")]
    [InlineData("laptop")]
    public void No_mark_or_an_unknown_one_means_the_machine_you_sit_at(string? mark) => Assert.Null(ViewedFrom.Parse(mark));

    [Fact]
    public void The_hints_name_the_machine_you_sit_at()
    {
        Assert.Contains("machine you sit at", ViewerHints.ManagedWhereYouSit, StringComparison.Ordinal);
        Assert.Contains("machine you sit at", ViewerHints.ScreensWhereYouSit, StringComparison.Ordinal);
    }
}
