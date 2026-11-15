using System.Collections.Concurrent;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class ViewerMarkTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly ConcurrentQueue<string> _log = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private Endpoint _endpoint = null!;
    private FleetDaemon _daemon = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await _stop.CancelAsync();
        await _running;
    }

    private sealed class FrozenClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private async Task<DaemonTests.TestClient> StartAsync(bool warm, TimeProvider? clock = null)
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        _daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = "fleet",
            WarmMenus = warm,
            Clock = clock ?? TimeProvider.System,
            Log = _log.Enqueue,
        });
        _running = _daemon.RunAsync(_stop.Token);

        var control = await ConnectAsync(ClientRoles.Control, null);
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Workspace = "techweb",
            Cwd = ".",
            Args = ["claude"],
        })).Ok);
        return control;
    }

    private async Task<DaemonTests.TestClient> ConnectAsync(
        string role, string? workspace, bool bridged = false, Action<Hello>? furnish = null)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(_endpoint, role, 100, 30, workspace, bridged, furnish);
        _clients.Add(client);
        return client;
    }

    private async Task<DaemonTests.TestClient> LinkAsync(string machine = "laptop")
    {
        var link = await ConnectAsync(ClientRoles.Attach, "techweb", furnish: hello => hello.Label = RemoteLink.Label("the ssh host"));
        Assert.True((await link.RequestAsync(new ControlRequest { Op = "set-label", Text = RemoteLink.Label(machine) })).Ok);
        return link;
    }

    private static string SshMark => $"ssh:{Environment.MachineName}";

    private Task<DaemonTests.TestClient> SshWindowAsync(string origin = "10.0.0.5") =>
        ConnectAsync(ClientRoles.Attach, "techweb", bridged: true, furnish: hello => hello.Origin = origin);

    private Task<DaemonTests.TestClient> LocalAsync() => ConnectAsync(ClientRoles.Attach, "techweb");

    private int Menus => _panes.Started.Count(p => p.Program == "fleet");

    private bool HeadStarted => _panes.Started.Any(p => p.Program == "fleet" && p.Args is ["head", ..]);

    private static async Task Eventually(Func<bool> condition, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow.Add(within ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition());
    }

    private async Task<string?> ColdMenuMarkAsync(DaemonTests.TestClient client)
    {
        await client.WaitForFramesAsync(1);
        await client.SendCommandAsync("menu");
        await Eventually(() => Menus == 1);
        return _panes.ByProgram("fleet")!.Env.GetValueOrDefault(ViewedFrom.Variable);
    }

    [Fact]
    public async Task A_cold_menu_for_a_remote_view_carries_the_machine_that_views_it()
    {
        await StartAsync(warm: false);

        Assert.Equal("view:laptop", await ColdMenuMarkAsync(await LinkAsync()));
    }

    [Fact]
    public async Task A_cold_menu_for_an_ssh_window_carries_the_machine_name_of_the_remote_whatever_address_it_came_from()
    {
        await StartAsync(warm: false);

        Assert.Equal(SshMark, await ColdMenuMarkAsync(await SshWindowAsync()));
    }

    [Fact]
    public async Task Ssh_windows_from_different_addresses_share_one_mark_and_one_warm_menu()
    {
        await StartAsync(warm: true);
        await (await SshWindowAsync("10.0.0.5")).WaitForFramesAsync(1);
        await (await SshWindowAsync("10.0.0.9")).WaitForFramesAsync(1);

        await Eventually(() => Menus == 1);

        Assert.Equal(SshMark, _panes.ByProgram("fleet")!.Env[ViewedFrom.Variable]);
    }

    [Fact]
    public async Task A_cold_menu_for_a_client_sitting_at_the_machine_carries_no_mark()
    {
        await StartAsync(warm: false);

        Assert.Null(await ColdMenuMarkAsync(await LocalAsync()));
    }

    [Fact]
    public async Task A_cold_menu_with_an_action_for_a_remote_view_carries_the_mark_too()
    {
        await StartAsync(warm: false);
        var link = await LinkAsync();
        await link.WaitForFramesAsync(1);

        await link.SendCommandAsync("menu", "list-agents");

        await Eventually(() => Menus == 1);
        Assert.Equal("view:laptop", _panes.ByProgram("fleet")!.Env[ViewedFrom.Variable]);
    }

    [Fact]
    public async Task The_warm_menu_of_a_workspace_seen_only_through_a_link_starts_with_the_mark()
    {
        await StartAsync(warm: true);
        await (await LinkAsync()).WaitForFramesAsync(1);

        await Eventually(() => Menus == 1);

        Assert.Equal("view:laptop", _panes.ByProgram("fleet")!.Env[ViewedFrom.Variable]);
    }

    [Fact]
    public async Task The_warm_menu_of_a_workspace_seen_only_from_the_machine_starts_without_a_mark()
    {
        await StartAsync(warm: true);
        await (await LocalAsync()).WaitForFramesAsync(1);

        await Eventually(() => Menus == 1);

        Assert.DoesNotContain(ViewedFrom.Variable, _panes.ByProgram("fleet")!.Env.Keys);
    }

    [Fact]
    public async Task The_warm_menu_of_a_workspace_seen_from_the_machine_and_through_a_link_starts_without_a_mark()
    {
        await StartAsync(warm: true);
        await (await LocalAsync()).WaitForFramesAsync(1);
        await (await LinkAsync()).WaitForFramesAsync(1);

        await Eventually(() => Menus == 1);

        Assert.DoesNotContain(ViewedFrom.Variable, _panes.ByProgram("fleet")!.Env.Keys);
    }

    private async Task<(string Pane, Task<ControlResponse> Waiting, FakePanes.FakePty Warm)> ParkedAndWaitingAsync(DaemonTests.TestClient control)
    {
        await Eventually(() => Menus == 1);
        var warm = _panes.ByProgram("fleet")!;
        var pane = warm.Env[FleetDaemon.PaneVariable];
        Assert.True((await control.RequestAsync(new ControlRequest { Op = "fit", Caller = pane, Cols = 40, Rows = 8 })).Ok);
        warm.Emit("WARM-MENU\nitems\n");
        var waiting = control.RequestAsync(new ControlRequest { Op = FleetDaemon.MenuWaitOp, Caller = pane });
        await Eventually(() => _daemon.Model.Float(pane) is { Parked: true, Hidden: false });
        return (pane, waiting, warm);
    }

    [Fact]
    public async Task A_menu_waiting_in_a_workspace_seen_only_through_a_link_is_told_the_mark_it_started_with_and_shows_at_once()
    {
        var control = await StartAsync(warm: true);
        await (await LinkAsync()).WaitForFramesAsync(1);
        var (pane, waiting, _) = await ParkedAndWaitingAsync(control);
        var link = _clients.Last(c => c != control);

        await link.SendCommandAsync("menu");

        var opened = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("view:laptop", opened.ViewedFrom);
        Assert.False(_daemon.Model.Float(pane)!.Hidden);
    }

    [Fact]
    public async Task A_menu_drawn_for_the_machine_and_opened_through_a_link_is_told_the_opener_and_stays_hidden_until_it_has_regrouped()
    {
        var control = await StartAsync(warm: true);
        var local = await LocalAsync();
        await local.WaitForFramesAsync(1);
        var link = await LinkAsync();
        await link.WaitForFramesAsync(1);
        var (pane, waiting, warm) = await ParkedAndWaitingAsync(control);

        await link.SendCommandAsync("menu");

        var opened = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("view:laptop", opened.ViewedFrom);
        Assert.True(_daemon.Model.Float(pane) is { Parked: false, Hidden: true }, "the ungrouped menu must not be seen by the remote view");

        var regrouping = System.Diagnostics.Stopwatch.StartNew();
        Assert.True((await control.RequestAsync(new ControlRequest { Op = "fit", Caller = pane, Cols = 44, Rows = 14 })).Ok);
        await Eventually(() => _daemon.Model.Float(pane) is { NeedsBaseline: false });
        Assert.True(_daemon.Model.Float(pane)!.Hidden, "a fit alone must not show the ungrouped menu");
        warm.Emit("\e[2J\e[HPROJECT on laptop\nquit\nMACHINE laptop\nsettings\nFLEET this machine\nremote machines\n");

        await Eventually(() => _daemon.Model.Float(pane) is { Hidden: false }, FleetDaemon.RevealAfterFit);
        Assert.True(regrouping.Elapsed < FleetDaemon.RevealAfterFit, "the regrouped menu shows on its redraw, not on a timeout");
    }

    // A parked menu that has not asked to be told its opener yet cannot regroup: it would show
    // the machine's own ungrouped menu to the remote view and run fleet rows on the remote.
    [Fact]
    public async Task A_menu_drawn_for_the_machine_that_is_not_waiting_yet_is_left_parked_and_a_remote_view_gets_a_fresh_menu_with_its_mark()
    {
        await StartAsync(warm: true);
        var local = await LocalAsync();
        await local.WaitForFramesAsync(1);
        var link = await LinkAsync();
        await link.WaitForFramesAsync(1);
        await Eventually(() => Menus == 1);
        var parked = _panes.ByProgram("fleet")!;
        var parkedPane = parked.Env[FleetDaemon.PaneVariable];
        Assert.DoesNotContain(ViewedFrom.Variable, parked.Env.Keys);

        await link.SendCommandAsync("menu");

        await Eventually(() => Menus == 2);
        var fresh = _panes.ByProgram("fleet")!;
        Assert.Equal("view:laptop", fresh.Env[ViewedFrom.Variable]);
        Assert.Equal(["menu", "--project", "techweb"], fresh.Args);
        Assert.True(_daemon.Model.Float(parkedPane) is { Parked: true });
    }

    [Fact]
    public async Task A_menu_drawn_for_the_machine_that_is_not_waiting_yet_still_opens_at_once_for_the_machine()
    {
        await StartAsync(warm: true);
        var local = await LocalAsync();
        await local.WaitForFramesAsync(1);
        await (await LinkAsync()).WaitForFramesAsync(1);
        await Eventually(() => Menus == 1);
        var parkedPane = _panes.ByProgram("fleet")!.Env[FleetDaemon.PaneVariable];

        await local.SendCommandAsync("menu");

        await Eventually(() => _daemon.Model.Float(parkedPane) is { Parked: false });
        Assert.Equal(1, Menus);
    }

    [Fact]
    public async Task A_menu_drawn_for_a_link_and_opened_by_the_same_link_is_not_hidden_for_a_redraw()
    {
        var control = await StartAsync(warm: true);
        var link = await LinkAsync();
        await link.WaitForFramesAsync(1);
        var (pane, waiting, warm) = await ParkedAndWaitingAsync(control);
        Assert.Equal("view:laptop", warm.Env[ViewedFrom.Variable]);

        await link.SendCommandAsync("menu");

        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(_daemon.Model.Float(pane) is { Parked: false, Hidden: false });
    }

    [Fact]
    public async Task A_menu_waiting_in_a_workspace_seen_by_the_machine_and_a_link_is_told_no_mark_when_the_machine_opens_it()
    {
        var control = await StartAsync(warm: true);
        var local = await LocalAsync();
        await local.WaitForFramesAsync(1);
        await (await LinkAsync()).WaitForFramesAsync(1);
        var (pane, waiting, _) = await ParkedAndWaitingAsync(control);

        await local.SendCommandAsync("menu");

        Assert.Null((await waiting.WaitAsync(TimeSpan.FromSeconds(5))).ViewedFrom);
        Assert.True(_daemon.Model.Float(pane) is { Parked: false, Hidden: false });
    }

    [Fact]
    public async Task A_label_change_kills_the_parked_menus_of_that_workspace_and_the_next_one_starts_with_the_new_mark_at_once()
    {
        var control = await StartAsync(warm: true, new FrozenClock());
        var link = await LinkAsync("before");
        await link.WaitForFramesAsync(1);
        await Eventually(() => Menus == 1);
        var first = _panes.ByProgram("fleet")!;
        Assert.Equal("view:before", first.Env[ViewedFrom.Variable]);

        Assert.True((await control.RequestAsync(new ControlRequest { Op = "set-label", Client = link.Id, Text = RemoteLink.Label("after") })).Ok);

        await Eventually(() => first.IsDisposed);
        await Eventually(() => Menus == 2);
        Assert.Equal("view:after", _panes.ByProgram("fleet")!.Env[ViewedFrom.Variable]);
    }

    [Fact]
    public async Task A_workspace_that_goes_from_the_machine_and_a_link_to_the_link_alone_is_warmed_again_with_the_mark()
    {
        await StartAsync(warm: true, new FrozenClock());
        var local = await LocalAsync();
        await local.WaitForFramesAsync(1);
        var link = await LinkAsync();
        await link.WaitForFramesAsync(1);
        await Eventually(() => Menus == 1);
        var mixed = _panes.ByProgram("fleet")!;
        Assert.DoesNotContain(ViewedFrom.Variable, mixed.Env.Keys);

        _clients.Remove(local);
        await local.DisposeAsync();

        await Eventually(() => mixed.IsDisposed);
        await Eventually(() => Menus == 2);
        Assert.Equal("view:laptop", _panes.ByProgram("fleet")!.Env[ViewedFrom.Variable]);
    }

    [Fact]
    public async Task A_workspace_that_goes_from_a_link_alone_to_the_link_and_the_machine_is_warmed_again_without_a_mark()
    {
        await StartAsync(warm: true, new FrozenClock());
        var link = await LinkAsync();
        await link.WaitForFramesAsync(1);
        await Eventually(() => Menus == 1);
        var marked = _panes.ByProgram("fleet")!;
        Assert.Equal("view:laptop", marked.Env[ViewedFrom.Variable]);

        await (await LocalAsync()).WaitForFramesAsync(1);

        await Eventually(() => marked.IsDisposed);
        await Eventually(() => Menus == 2);
        Assert.DoesNotContain(ViewedFrom.Variable, _panes.ByProgram("fleet")!.Env.Keys);
    }

    [Fact]
    public async Task A_warm_menu_is_not_started_for_a_workspace_until_the_label_of_a_client_showing_it_is_confirmed()
    {
        var control = await StartAsync(warm: true, new FrozenClock());
        var link = await ConnectAsync(ClientRoles.Attach, "techweb", furnish: hello => hello.Label = RemoteLink.Label("the ssh host"));
        await link.WaitForFramesAsync(1);
        Assert.True((await control.RequestAsync(new ControlRequest { Op = "ping" })).Ok);
        Assert.Equal(0, Menus);

        Assert.True((await link.RequestAsync(new ControlRequest { Op = "set-label", Text = RemoteLink.Label("laptop") })).Ok);

        await Eventually(() => Menus == 1);
        Assert.Equal("view:laptop", _panes.ByProgram("fleet")!.Env[ViewedFrom.Variable]);
    }

    [Fact]
    public async Task A_link_that_reconnects_while_the_old_connection_lingers_keeps_the_parked_menu_it_had()
    {
        await StartAsync(warm: true, new FrozenClock());
        var first = await LinkAsync();
        await first.WaitForFramesAsync(1);
        await Eventually(() => Menus == 1);
        var parked = _panes.ByProgram("fleet")!;

        var again = await ConnectAsync(ClientRoles.Attach, "techweb", furnish: hello => hello.Label = RemoteLink.Label("the ssh host"));
        await again.WaitForFramesAsync(1);
        Assert.True((await again.RequestAsync(new ControlRequest { Op = "ping" })).Ok);
        Assert.False(parked.IsDisposed);

        Assert.True((await again.RequestAsync(new ControlRequest { Op = "set-label", Text = RemoteLink.Label("laptop") })).Ok);
        _clients.Remove(first);
        await first.DisposeAsync();
        await Eventually(() => _log.Contains($"client {first.Id} detached"));
        Assert.True((await again.RequestAsync(new ControlRequest { Op = "ping" })).Ok);

        Assert.False(parked.IsDisposed);
        Assert.Equal(1, Menus);
    }

    [Fact]
    public async Task The_head_chord_from_an_ssh_window_opens_the_menu_with_the_head_action_instead_of_toggling_the_head()
    {
        await StartAsync(warm: false);
        var ssh = await SshWindowAsync();
        await ssh.WaitForFramesAsync(1);

        await ssh.SendCommandAsync("head");

        await Eventually(() => Menus == 1);
        Assert.Equal(["menu", "--project", "techweb", "--action", "head-voice"], _panes.ByProgram("fleet")!.Args);
        Assert.Equal(SshMark, _panes.ByProgram("fleet")!.Env[ViewedFrom.Variable]);
        Assert.False(HeadStarted);
    }

    // What fleetd starts for an ssh window, fed to the rule the menu applies before anything runs:
    // every fleet-level screen asked for there ends in the hint, never in a screen of the remote.
    [Theory]
    [InlineData("menu", "switch-project")]
    [InlineData("menu", "notifications")]
    [InlineData("menu", "remotes")]
    [InlineData("menu", "save-session")]
    [InlineData("menu", "this-machine-settings")]
    [InlineData("switch-project", null)]
    [InlineData("head", null)]
    [InlineData("head", "voice")]
    public async Task A_fleet_level_chord_from_an_ssh_window_starts_a_menu_that_only_hints(string command, string? arg)
    {
        await StartAsync(warm: false);
        var ssh = await SshWindowAsync();
        await ssh.WaitForFramesAsync(1);

        await ssh.SendCommandAsync(command, arg);

        await Eventually(() => Menus == 1);
        var menu = _panes.ByProgram("fleet")!;
        var viewer = ViewedFrom.Parse(menu.Env.GetValueOrDefault(ViewedFrom.Variable));
        var asked = Fleet.Shared.Keymap.FleetActionIds.Parse(menu.Args[^1]);
        Assert.Equal("--action", menu.Args[^2]);
        Assert.Equal(new ViewedFrom(Fleet.Shared.Viewing.Enums.ViewKind.Ssh, Environment.MachineName), viewer);
        Assert.Equal(Fleet.Shared.Viewing.Enums.ScreenRoute.Hint, Fleet.Shared.Viewing.ViewerScreens.Route(viewer, asked));
        Assert.False(HeadStarted);
    }

    [Fact]
    public async Task The_head_chord_from_a_client_sitting_at_the_machine_still_toggles_the_head()
    {
        await StartAsync(warm: false);
        var local = await LocalAsync();
        await local.WaitForFramesAsync(1);

        await local.SendCommandAsync("head");

        await Eventually(() => HeadStarted);
        Assert.DoesNotContain(_panes.Started, p => p.Args.Contains("menu"));
    }
}
