using System.Reflection;
using System.Text.RegularExpressions;
using Fleet.Cli.Commands;
using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Sessions.SaveSession;
using Fleet.Ports.Sessions.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;

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

    [Fact]
    public void A_screen_is_handed_back_only_by_the_dispatch()
    {
        var source = MenuSource();

        Assert.Equal(1, Regex.Count(source, @"EmbeddedWiring\.HandBack"));
        Assert.Contains("new MenuDispatch(ViewerWiring.Current, EmbeddedWiring.HandBack)", source, StringComparison.Ordinal);
    }

    private sealed class Start
    {
        public List<string> Ran { get; } = [];

        public Task<int> For(string? named, bool found, FleetAction requested) =>
            MenuCommand.StartAsync(
                named,
                found,
                requested,
                (shown, screen) => Done($"this machine: {screen} for {shown}", 0),
                startNew => Done(startNew ? "new project" : "pick project", 0),
                () => Done("menu", 0),
                Ran.Add);

        private Task<int> Done(string what, int code)
        {
            Ran.Add(what);
            return Task.FromResult(code);
        }
    }

    public static TheoryData<FleetAction> OwnedScreens { get; } = [.. ViewerScreens.Owned];

    [Theory]
    [MemberData(nameof(OwnedScreens))]
    public async Task A_menu_started_for_a_view_of_a_remote_with_an_owned_screen_runs_that_screen_on_this_machine_and_picks_no_project(FleetAction screen)
    {
        var start = new Start();

        await start.For("@far", found: false, screen);

        Assert.Equal([$"this machine: {screen} for @far"], start.Ran);
    }

    [Theory]
    [InlineData("@far", false, FleetAction.ListAgents, "pick project")]
    [InlineData("@far", false, FleetAction.None, "pick project")]
    [InlineData(null, false, FleetAction.Remotes, "pick project")]
    [InlineData("gone", false, FleetAction.Remotes, "pick project")]
    [InlineData("homelab", true, FleetAction.OpenProject, "pick project")]
    [InlineData("homelab", true, FleetAction.NewProject, "new project")]
    [InlineData("homelab", true, FleetAction.Remotes, "menu")]
    [InlineData("homelab", true, FleetAction.None, "menu")]
    [InlineData("homelab", true, FleetAction.EditShowMenuKeys, "menu")]
    public async Task Any_other_start_of_the_menu_goes_where_it_went_before(string? named, bool found, FleetAction requested, string goes)
    {
        var start = new Start();

        await start.For(named, found, requested);

        Assert.Equal([goes], start.Ran);
    }

    [Fact]
    public void This_machines_log_keeps_the_lines_of_every_project_and_of_none_newest_first()
    {
        string[] lines =
        [
            "2026-08-10T09:00:00.0000000+02:00 [techweb] started agent backend/dev",
            "2026-08-10T09:01:00.0000000+02:00 [other] pulled something",
            "2026-08-10T09:02:00.0000000+02:00 fleetd: listening",
        ];

        Assert.Equal(["", "other", "techweb"], MenuCommand.ThisMachinesLog(lines).Select(e => e.Project));
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
