using System.Reflection;
using System.Text.RegularExpressions;
using Fleet.Cli.Commands;
using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Sessions.SaveSession;
using Fleet.Ports.Sessions.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Tests.Cli;

public class MenuRemoteViewTests
{
    public static TheoryData<FleetAction> Owned { get; } = [.. ViewerScreens.Owned];

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

    public static TheoryData<FleetAction> ThisMachinesRows { get; } = [.. FleetMenus.Actions(RemoteMenus.ThisMachine)];

    [Theory]
    [MemberData(nameof(ThisMachinesRows))]
    [InlineData(FleetAction.SwitchProject)]
    [InlineData(FleetAction.Notifications)]
    [InlineData(FleetAction.ThisMachineSettings)]
    public void Every_fleet_row_and_every_row_of_this_machines_settings_has_a_screen_that_needs_no_project(FleetAction action) =>
        Assert.Contains(action, MenuCommand.ScreensForRemoteView);

    [Fact]
    public void The_screens_for_a_view_of_a_remote_are_read_from_the_handlers_not_kept_as_a_second_list()
    {
        var source = MenuSource();
        var screens = source.IndexOf("ScreensForRemoteView =>", StringComparison.Ordinal);

        Assert.True(screens > 0);
        Assert.Contains("OnThisMachine.Keys", source[screens..source.IndexOf(';', screens)], StringComparison.Ordinal);
        Assert.DoesNotContain("switch (screen)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_saved_from_a_view_of_a_remote_is_first_named_after_the_project_shown()
    {
        WindowEntry[] window = [new("techweb", null, false), new("homelab-api", "red@homelab", true)];

        Assert.Equal("homelab-api", MenuCommand.SessionName(null, window));
        Assert.Equal("techweb", MenuCommand.SessionName(null, [window[0]]));
        Assert.Equal("techweb", MenuCommand.SessionName("techweb", window));
    }

    [Fact]
    public void A_session_saved_from_a_view_of_a_remote_records_the_remote_project_with_its_host()
    {
        WindowEntry[] window = [new("techweb", null, false), new("homelab-api", "red@homelab", true)];

        var session = SaveSessionHandler.For(MenuCommand.SessionName(null, window), window);

        Assert.Equal(new SessionProject("homelab-api", "red@homelab"), session.Showing);
        Assert.Contains(new SessionProject("homelab-api", "red@homelab"), session.Projects);
    }

    // The body of `fleet menu` runs inside Terminal.Gui and has no test of its own. These pin its
    // call sites: every action picked on the remote goes through MenuDispatch (tested in
    // MenuDispatchTests), and a menu started for a view of a remote goes to this machine's screens.
    [Fact]
    public void Every_action_the_menu_runs_goes_through_the_dispatch_that_knows_the_viewer()
    {
        var source = MenuSource();

        Assert.Equal(2, Regex.Count(source, @"\bPerform\("));
        Assert.Contains("action => Perform(app, keymap, keymaps, adder, projects, project, action)", source, StringComparison.Ordinal);
        Assert.Matches(@"await dispatch\s*\.RunAsync\(\s*chosen,\s*action => Perform\(", source);
    }

    [Fact]
    public void A_screen_is_handed_back_only_by_the_dispatch()
    {
        var source = MenuSource();

        Assert.Equal(1, Regex.Count(source, @"EmbeddedWiring\.HandBack"));
        Assert.Contains("new MenuDispatch(ViewerWiring.Current, EmbeddedWiring.HandBack)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_requested_action_is_settled_for_the_viewer_before_a_toggle_runs_headless_or_the_ui_starts()
    {
        var source = MenuSource();
        var run = source.IndexOf("public static async Task<int> RunAsync", StringComparison.Ordinal);
        var settled = source.IndexOf("dispatch.Settle(requested)", run, StringComparison.Ordinal);
        var headless = source.IndexOf("RunsHeadless(requested)", run, StringComparison.Ordinal);

        Assert.True(settled > run, "RunAsync should settle the requested action for the viewer");
        Assert.True(settled < headless, "a viewer's toggle must be settled before it can run headless on the remote");
    }

    [Fact]
    public void A_menu_started_for_a_view_of_a_remote_runs_this_machines_screen_before_it_looks_for_a_project_to_pick()
    {
        var source = MenuSource();
        var run = source.IndexOf("public static async Task<int> RunAsync", StringComparison.Ordinal);
        var here = source.IndexOf("return await RunOnThisMachineAsync(shown, screen, projects)", run, StringComparison.Ordinal);
        var pick = source.IndexOf("PickProjectCommand.RunAsync", run, StringComparison.Ordinal);

        Assert.True(here > run && here < pick);
        Assert.Contains("await OnThisMachine[chosen](", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_menu_drawn_for_one_viewer_and_opened_by_another_regroups()
    {
        var view = new ViewedFrom(ViewKind.View, "homelab");

        Assert.True(MenuCommand.NeedsRegroup(null, view));
        Assert.True(MenuCommand.NeedsRegroup(view, null));
        Assert.True(MenuCommand.NeedsRegroup(view, new ViewedFrom(ViewKind.View, "barn")));
        Assert.True(MenuCommand.NeedsRegroup(view, new ViewedFrom(ViewKind.Ssh, "homelab")));
    }

    [Fact]
    public void A_menu_opened_by_the_viewer_it_was_drawn_for_does_not_redraw()
    {
        Assert.False(MenuCommand.NeedsRegroup(null, null));
        Assert.False(MenuCommand.NeedsRegroup(new ViewedFrom(ViewKind.View, "homelab"), new ViewedFrom(ViewKind.View, "homelab")));
    }

    private static string MenuSource() =>
        File.ReadAllText(Path.Combine(
            typeof(MenuRemoteViewTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(a => a.Key == "RepoRoot")
                .Value!,
            "src", "Fleet", "Cli", "Commands", "MenuCommand.cs"));

    [Fact]
    public void An_owned_screen_with_no_screen_on_this_machine_closes_instead_of_opening_another() =>
        Assert.Equal(FleetAction.Close, MenuCommand.ScreenForRemoteView("@far", FleetAction.Remotes, [FleetAction.SwitchProject]));
}
