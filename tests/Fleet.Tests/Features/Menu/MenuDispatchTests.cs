using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Tests.Features.Menu;

// Drives "chosen action + viewer + what the viewer announced -> what ran where" without the
// Terminal.Gui loop. The fake hand-back answers like fleetd does: only an announced id is taken.
public class MenuDispatchTests
{
    private static readonly ViewedFrom View = new(ViewKind.View, "homelab");

    private static readonly ViewedFrom Ssh = new(ViewKind.Ssh, "homelab-box");

    private sealed class Machines(ViewedFrom? viewer, IReadOnlyList<string> announced)
    {
        public List<FleetAction> RanOnTheRemote { get; } = [];

        public List<string> HandedBack { get; } = [];

        public List<string> Asked { get; } = [];

        public int Hints { get; private set; }

        public ViewedFrom? Viewer { get; set; } = viewer;

        public MenuDispatch Dispatch => new(() => Viewer, id =>
        {
            Asked.Add(id);
            if (!announced.Contains(id))
            {
                return false;
            }

            HandedBack.Add(id);
            return true;
        });

        public Task<(ScreenRoute Route, int Code)> Choose(FleetAction action, int code = 0) =>
            Dispatch.RunAsync(
                action,
                ran =>
                {
                    RanOnTheRemote.Add(ran);
                    return Task.FromResult(code);
                },
                () => Hints++);
    }

    public static TheoryData<FleetAction> FleetRows { get; } =
        [.. RemoteMenus.For(View, FleetAction.None).Single(s => s.Header == RemoteMenus.ThisMachineTitle).Actions];

    public static TheoryData<FleetAction> RemoteRows { get; } =
    [
        .. FleetMenus.Actions(RemoteMenus.For(View, FleetAction.None))
            .Concat(FleetMenus.Actions(RemoteMenus.For(View, FleetAction.OpenSettings)))
            .Concat(FleetMenus.Actions(RemoteMenus.For(View, FleetAction.OpenFleetConfigMenu)))
            .Where(a => ActionGroups.Of(a) != ActionGroup.ThisMachine && !FleetMenus.IsSubmenu(a)),
    ];

    public static TheoryData<FleetAction> Owned { get; } = [.. ViewerScreens.Owned];

    [Theory]
    [MemberData(nameof(FleetRows))]
    public async Task A_row_under_fleet_this_machine_in_a_remote_view_goes_back_to_the_machine_you_sit_at_and_runs_nothing_on_the_remote(FleetAction action)
    {
        var machines = new Machines(View, ViewerScreens.Ids);

        var (route, code) = await machines.Choose(action);

        Assert.Equal((ScreenRoute.HandBack, 0), (route, code));
        Assert.Equal([FleetActionIds.For(action)], machines.HandedBack);
        Assert.Empty(machines.RanOnTheRemote);
        Assert.Equal(0, machines.Hints);
    }

    [Theory]
    [MemberData(nameof(RemoteRows))]
    public async Task A_row_under_project_or_machine_in_a_remote_view_runs_on_the_remote_and_is_not_offered_back(FleetAction action)
    {
        var machines = new Machines(View, ViewerScreens.Ids);

        var (route, code) = await machines.Choose(action, code: 3);

        Assert.Equal((ScreenRoute.Here, 3), (route, code));
        Assert.Equal([action], machines.RanOnTheRemote);
        Assert.Empty(machines.Asked);
        Assert.Equal(0, machines.Hints);
    }

    [Theory]
    [InlineData(FleetAction.ViewLogs)]
    [InlineData(FleetAction.UpdateFleet)]
    [InlineData(FleetAction.ShowVersion)]
    [InlineData(FleetAction.WhatsNew)]
    public async Task The_log_update_version_and_whats_new_of_a_remote_view_stay_the_remotes(FleetAction action)
    {
        var machines = new Machines(View, ViewerScreens.Ids);

        await machines.Choose(action);

        Assert.Equal([action], machines.RanOnTheRemote);
        Assert.Empty(machines.HandedBack);
    }

    [Theory]
    [InlineData(FleetAction.Remotes)]
    [InlineData(FleetAction.SaveSession)]
    [InlineData(FleetAction.ThisMachineSettings)]
    [InlineData(FleetAction.EditTheme)]
    [InlineData(FleetAction.EditShowMenuKeys)]
    public async Task A_fleet_row_the_viewer_did_not_announce_shows_the_hint_and_is_neither_handed_back_nor_run_on_the_remote(FleetAction action)
    {
        var older = new Machines(View, ViewerScreens.Legacy);

        var (route, _) = await older.Choose(action);

        Assert.Equal(ScreenRoute.Hint, route);
        Assert.Equal(1, older.Hints);
        Assert.Empty(older.HandedBack);
        Assert.Empty(older.RanOnTheRemote);
    }

    [Theory]
    [InlineData(FleetAction.SwitchProject)]
    [InlineData(FleetAction.Notifications)]
    public async Task An_older_viewer_still_gets_switch_project_and_notifications_back(FleetAction action)
    {
        var older = new Machines(View, ViewerScreens.Legacy);

        var (route, _) = await older.Choose(action);

        Assert.Equal(ScreenRoute.HandBack, route);
        Assert.Equal([FleetActionIds.For(action)], older.HandedBack);
        Assert.Equal(0, older.Hints);
    }

    [Theory]
    [MemberData(nameof(Owned))]
    [InlineData(FleetAction.OpenHeadVoice)]
    public async Task A_fleet_screen_asked_for_in_an_ssh_window_shows_the_hint_and_opens_nothing_of_the_remote(FleetAction action)
    {
        var machines = new Machines(Ssh, ViewerScreens.Ids);

        var (route, code) = await machines.Choose(action);

        Assert.Equal((ScreenRoute.Hint, 0), (route, code));
        Assert.Equal(1, machines.Hints);
        Assert.Empty(machines.Asked);
        Assert.Empty(machines.RanOnTheRemote);
    }

    public static TheoryData<ViewKind, bool> Viewers { get; } = new()
    {
        { ViewKind.View, true },
        { ViewKind.View, false },
        { ViewKind.Ssh, true },
        { ViewKind.Ssh, false },
    };

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Remote_machines_never_runs_on_the_remote_for_any_viewer(ViewKind kind, bool announced)
    {
        var machines = new Machines(new ViewedFrom(kind, "homelab"), announced ? ViewerScreens.Ids : ViewerScreens.Legacy);

        await machines.Choose(FleetAction.Remotes);

        Assert.Empty(machines.RanOnTheRemote);
    }

    [Theory]
    [InlineData(FleetAction.EditShowMenuKeys)]
    [InlineData(FleetAction.EditButtonHints)]
    public void An_owned_toggle_is_settled_away_from_the_remote_for_a_viewer_before_anything_runs(FleetAction toggle)
    {
        Assert.True(FleetMenus.IsToggle(toggle));
        Assert.Equal(ScreenRoute.HandBack, new Machines(View, ViewerScreens.Ids).Dispatch.Settle(toggle));
        Assert.Equal(ScreenRoute.Hint, new Machines(View, ViewerScreens.Legacy).Dispatch.Settle(toggle));
        Assert.Equal(ScreenRoute.Hint, new Machines(Ssh, ViewerScreens.Ids).Dispatch.Settle(toggle));
        Assert.Equal(ScreenRoute.Here, new Machines(null, ViewerScreens.Ids).Dispatch.Settle(toggle));
    }

    // No mark does not prove nobody views the menu: a fleetd older than this binary (after
    // `fleet update`, before fleetd restarts) sets none. The two screens every released fleet
    // hands back are therefore still offered to the daemon, which grants them to a remote view
    // and refuses them for someone sitting at the machine.
    public static TheoryData<FleetAction> LegacyScreens { get; } = [FleetAction.SwitchProject, FleetAction.Notifications];

    public static TheoryData<FleetAction> NeverOfferedWithoutAMark { get; } =
    [
        .. ViewerScreens.Owned.Where(a => a is not (FleetAction.SwitchProject or FleetAction.Notifications)),
        FleetAction.QuitFleet,
        FleetAction.ViewLogs,
        FleetAction.OpenHeadVoice,
        FleetAction.OpenSettings,
    ];

    [Theory]
    [MemberData(nameof(LegacyScreens))]
    public async Task With_no_mark_switch_project_and_notifications_are_still_offered_and_go_back_when_the_daemon_grants_them(FleetAction action)
    {
        var behindAnOlderDaemon = new Machines(null, ViewerScreens.Legacy);

        var (route, code) = await behindAnOlderDaemon.Choose(action);

        Assert.Equal((ScreenRoute.HandBack, 0), (route, code));
        Assert.Equal([FleetActionIds.For(action)], behindAnOlderDaemon.HandedBack);
        Assert.Empty(behindAnOlderDaemon.RanOnTheRemote);
        Assert.Equal(0, behindAnOlderDaemon.Hints);
    }

    [Theory]
    [MemberData(nameof(LegacyScreens))]
    public async Task With_no_mark_switch_project_and_notifications_run_here_when_the_daemon_refuses_as_it_does_for_someone_sitting_at_the_machine(FleetAction action)
    {
        var sittingAtTheMachine = new Machines(null, []);

        var (route, _) = await sittingAtTheMachine.Choose(action);

        Assert.Equal(ScreenRoute.Here, route);
        Assert.Equal([action], sittingAtTheMachine.RanOnTheRemote);
        Assert.Equal([FleetActionIds.For(action)], sittingAtTheMachine.Asked);
        Assert.Equal(0, sittingAtTheMachine.Hints);
    }

    [Theory]
    [MemberData(nameof(NeverOfferedWithoutAMark))]
    public async Task With_no_mark_every_other_action_runs_here_and_is_never_offered_even_to_a_daemon_that_would_take_it(FleetAction action)
    {
        var machines = new Machines(null, ViewerScreens.Ids);

        var (route, _) = await machines.Choose(action);

        Assert.Equal(ScreenRoute.Here, route);
        Assert.Equal([action], machines.RanOnTheRemote);
        Assert.Empty(machines.Asked);
        Assert.Equal(0, machines.Hints);
    }

    [Fact]
    public async Task The_viewer_is_read_when_the_action_is_chosen_so_a_regrouped_menu_routes_for_its_opener()
    {
        var machines = new Machines(null, ViewerScreens.Ids);
        var dispatch = machines.Dispatch;
        machines.Viewer = View;

        var (route, _) = await dispatch.RunAsync(FleetAction.Remotes, _ => Task.FromResult(0), () => { });

        Assert.Equal(ScreenRoute.HandBack, route);
    }
}
