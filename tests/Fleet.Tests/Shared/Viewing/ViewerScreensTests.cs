using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Tests.Shared.Viewing;

public class ViewerScreensTests
{
    [Fact]
    public void The_machine_you_sit_at_owns_every_fleet_level_screen()
    {
        Assert.Equal(
            [
                FleetAction.SwitchProject,
                FleetAction.Notifications,
                FleetAction.Remotes,
                FleetAction.SaveSession,
                FleetAction.EditKeybinds,
                FleetAction.EditTheme,
                FleetAction.EditShowMenuKeys,
                FleetAction.EditButtonHints,
                FleetAction.ThisMachineSettings,
            ],
            ViewerScreens.Owned);
        Assert.Equal(
            ["switch-project", "notifications", "remotes", "save-session", "keybinds", "theme", "show-menu-keys", "button-hints", "this-machine-settings"],
            ViewerScreens.Ids);
    }

    [Fact]
    public void The_owned_screens_and_the_head_are_exactly_what_the_ownership_table_gives_the_machine_you_sit_at() =>
        Assert.Equal(
            ActionGroups.In(ActionGroup.ThisMachine).Order(),
            ViewerScreens.Owned.Append(ViewerScreens.Head).Order());

    [Fact]
    public void An_older_machine_is_still_only_handed_switch_project_and_notifications() =>
        Assert.Equal(["switch-project", "notifications"], ViewerScreens.Legacy);

    [Theory]
    [InlineData("show-menu-keys", "this-machine-settings")]
    [InlineData("button-hints", "this-machine-settings")]
    [InlineData("save-session", "save-session")]
    [InlineData("remotes", "remotes")]
    [InlineData("theme", "theme")]
    [InlineData("this-machine-settings", "this-machine-settings")]
    public void A_screen_handed_back_unasked_opens_a_screen_and_never_flips_a_setting(string handed, string opened) =>
        Assert.Equal(opened, ViewerScreens.OpenedWhenHandedBack(handed));

    [Theory]
    [InlineData("switch-project", true)]
    [InlineData("notifications", true)]
    [InlineData("remotes", true)]
    [InlineData("save-session", true)]
    [InlineData("this-machine-settings", true)]
    [InlineData("head-voice", false)]
    [InlineData("viewlogs", false)]
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
    [InlineData(FleetAction.SaveSession, ScreenRoute.Here)]
    [InlineData(FleetAction.EditShowMenuKeys, ScreenRoute.Here)]
    [InlineData(FleetAction.OpenHeadVoice, ScreenRoute.Here)]
    [InlineData(FleetAction.QuitFleet, ScreenRoute.Here)]
    public void A_menu_on_the_machine_you_sit_at_runs_everything_here(FleetAction action, ScreenRoute route) =>
        Assert.Equal(route, ViewerScreens.Route(null, action));

    [Theory]
    [InlineData(FleetAction.Remotes, ScreenRoute.HandBack)]
    [InlineData(FleetAction.SwitchProject, ScreenRoute.HandBack)]
    [InlineData(FleetAction.SaveSession, ScreenRoute.HandBack)]
    [InlineData(FleetAction.EditKeybinds, ScreenRoute.HandBack)]
    [InlineData(FleetAction.EditTheme, ScreenRoute.HandBack)]
    [InlineData(FleetAction.EditShowMenuKeys, ScreenRoute.HandBack)]
    [InlineData(FleetAction.EditButtonHints, ScreenRoute.HandBack)]
    [InlineData(FleetAction.ThisMachineSettings, ScreenRoute.HandBack)]
    [InlineData(FleetAction.OpenHeadVoice, ScreenRoute.Hint)]
    [InlineData(FleetAction.ViewLogs, ScreenRoute.Here)]
    [InlineData(FleetAction.UpdateFleet, ScreenRoute.Here)]
    [InlineData(FleetAction.ShowVersion, ScreenRoute.Here)]
    [InlineData(FleetAction.WhatsNew, ScreenRoute.Here)]
    [InlineData(FleetAction.QuitFleet, ScreenRoute.Here)]
    public void A_menu_drawn_for_a_remote_view_hands_owned_screens_back(FleetAction action, ScreenRoute route) =>
        Assert.Equal(route, ViewerScreens.Route(new ViewedFrom(ViewKind.View, " @home"), action));

    [Theory]
    [InlineData(FleetAction.Remotes, ScreenRoute.Hint)]
    [InlineData(FleetAction.Notifications, ScreenRoute.Hint)]
    [InlineData(FleetAction.SwitchProject, ScreenRoute.Hint)]
    [InlineData(FleetAction.SaveSession, ScreenRoute.Hint)]
    [InlineData(FleetAction.EditShowMenuKeys, ScreenRoute.Hint)]
    [InlineData(FleetAction.ThisMachineSettings, ScreenRoute.Hint)]
    [InlineData(FleetAction.OpenHeadVoice, ScreenRoute.Hint)]
    [InlineData(FleetAction.ViewLogs, ScreenRoute.Here)]
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
