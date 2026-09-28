using System.Collections.Concurrent;
using System.Text;
using Fleet.Features.Projects.LocateProject;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.SwitchProject;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class DaemonTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly List<TestClient> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private Endpoint _endpoint = null!;
    private FleetDaemon _daemon = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        _daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = "fleet",
        });
        _running = _daemon.RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await _stop.CancelAsync();
        await _running;
    }

    private async Task<TestClient> AttachAsync(int cols = 60, int rows = 12, string? workspace = null)
    {
        var client = await TestClient.ConnectAsync(_endpoint, ClientRoles.Attach, cols, rows, workspace);
        _clients.Add(client);
        return client;
    }

    private async Task<TestClient> ControlAsync()
    {
        var client = await TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null);
        _clients.Add(client);
        return client;
    }

    private static async Task<string> SpawnAsync(TestClient control, string workspace, string program)
    {
        var spawned = await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = workspace,
            NewWindow = true,
            Cwd = ".",
            Args = [program],
        });

        Assert.True(spawned.Ok, spawned.Error);
        return spawned.Pane!;
    }

    [Fact]
    public async Task A_switch_by_one_client_redraws_only_that_client_from_existing_screens()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "claude-techweb");
        await SpawnAsync(control, "fleet", "claude-fleet");
        _panes.ByProgram("claude-techweb")!.Emit("techweb screen");
        _panes.ByProgram("claude-fleet")!.Emit("fleet screen");

        var laptop = await AttachAsync(workspace: "techweb");
        var desktop = await AttachAsync(workspace: "fleet");
        await laptop.WaitForAsync("techweb screen");
        await desktop.WaitForAsync("fleet screen");
        var started = _panes.Started.Count;
        var desktopFrames = desktop.FrameCount;

        var shown = await laptop.RequestAsync(new ControlRequest { Op = "show", Workspace = "fleet" });

        Assert.True(shown.Ok, shown.Error);
        await laptop.WaitForAsync("fleet screen");
        await Task.Delay(150);
        Assert.Equal(desktopFrames, desktop.FrameCount);
        Assert.Equal(started, _panes.Started.Count);
        Assert.Equal(0, _panes.Disposed);
    }

    [Fact]
    public async Task Panes_and_processes_survive_hide_show_round_trips()
    {
        var control = await ControlAsync();
        var techweb = await SpawnAsync(control, "techweb", "a");
        var fleet = await SpawnAsync(control, "fleet", "b");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);
        var before = await control.RequestAsync(new ControlRequest { Op = "list-panes" });

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await client.RequestAsync(new ControlRequest { Op = "show", Workspace = "fleet" })).Ok);
            Assert.True((await client.RequestAsync(new ControlRequest { Op = "show", Workspace = "techweb" })).Ok);
        }

        var after = await control.RequestAsync(new ControlRequest { Op = "list-panes" });
        Assert.Equal(before.Panes!.Select(p => p.Id), after.Panes!.Select(p => p.Id));
        Assert.Equal([techweb, fleet], after.Panes!.Select(p => p.Id));
        Assert.Equal(2, _panes.Started.Count);
        Assert.Equal(0, _panes.Disposed);
    }

    [Fact]
    public async Task A_control_connection_without_a_client_cannot_change_any_view()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "a");
        await SpawnAsync(control, "fleet", "b");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        var shown = await control.RequestAsync(new ControlRequest { Op = "show", Workspace = "fleet" });

        Assert.False(shown.Ok);
        var listed = await client.RequestAsync(new ControlRequest { Op = "list-workspaces" });
        Assert.True(listed.Workspaces!.Single(w => w.Name == "techweb").ShownHere);
    }

    [Fact]
    public async Task Showing_reports_whose_view_it_changed_through_list_workspaces()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "a");
        await SpawnAsync(control, "fleet", "b");
        var laptop = await AttachAsync(workspace: "techweb");
        var desktop = await AttachAsync(workspace: "techweb");
        await laptop.WaitForFramesAsync(1);

        await control.RequestAsync(new ControlRequest { Op = "show", Workspace = "fleet", Client = laptop.Id });

        var fromLaptop = await control.RequestAsync(new ControlRequest { Op = "list-workspaces", Client = laptop.Id });
        var fromDesktop = await control.RequestAsync(new ControlRequest { Op = "list-workspaces", Client = desktop.Id });
        Assert.True(fromLaptop.Workspaces!.Single(w => w.Name == "fleet").ShownHere);
        Assert.True(fromDesktop.Workspaces!.Single(w => w.Name == "techweb").ShownHere);
    }

    [Fact]
    public async Task Keys_go_to_the_focused_pane_of_the_client_that_typed_them()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "a");
        await SpawnAsync(control, "fleet", "b");
        var laptop = await AttachAsync(workspace: "techweb");
        var desktop = await AttachAsync(workspace: "fleet");
        await laptop.WaitForFramesAsync(1);
        await desktop.WaitForFramesAsync(1);

        await laptop.SendKeyAsync("x");
        await desktop.SendKeyAsync("y");

        await Eventually(() => _panes.ByProgram("a")!.Written == "x" && _panes.ByProgram("b")!.Written == "y");
    }

    [Fact]
    public async Task A_hidden_agent_keeps_receiving_output_while_no_client_draws_it()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "claude");
        var agent = await SpawnAsync(control, "techweb", "agent");
        var moved = await control.RequestAsync(new ControlRequest
        {
            Op = "move",
            Pane = agent,
            Workspace = FleetWorkspaces.HiddenFor("techweb"),
        });
        Assert.True(moved.Ok, moved.Error);
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        _panes.ByProgram("agent")!.Emit("still working");

        await Eventually(async () =>
            (await control.RequestAsync(new ControlRequest { Op = "get-text", Pane = agent })).Text == "still working");
        Assert.DoesNotContain("still working", client.AllText);
    }

    [Fact]
    public async Task The_menu_opens_as_a_float_in_the_workspace_you_are_in_and_gives_the_keys_back()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "claude");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendCommandAsync("menu");
        await client.SendCommandAsync("menu");
        await client.SendKeyAsync("m");

        await Eventually(() => _panes.ByProgram("fleet") is { Written: "m" });
        var menu = _panes.ByProgram("fleet")!;
        Assert.Equal(["menu", "--project", "techweb"], menu.Args);
        Assert.Equal(client.Id, menu.Env[FleetDaemon.ClientVariable]);
        Assert.Single(_panes.Started, p => p.Program == "fleet");
        var panes = await control.RequestAsync(new ControlRequest { Op = "list-panes" });
        var listed = panes.Panes!.Single(p => p.Id == menu.Env[FleetDaemon.PaneVariable]);
        Assert.Equal(("techweb", "float", FleetDaemon.MenuTitle), (listed.Session, listed.Tab, listed.Title));

        menu.Exit();
        await Eventually(async () => (await control.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes!.Count == 1);
        await client.SendKeyAsync("c");

        await Eventually(() => _panes.ByProgram("claude")!.Written == "c");
    }

    [Fact]
    public async Task Without_a_workspace_on_screen_the_menu_falls_back_to_the_clients_overlay()
    {
        var control = await ControlAsync();
        var client = await AttachAsync();
        await client.WaitForFramesAsync(1);

        await client.SendCommandAsync("menu");

        await Eventually(() => _panes.ByProgram("fleet") is not null);
        var menu = _panes.ByProgram("fleet")!;
        var panes = await control.RequestAsync(new ControlRequest { Op = "list-panes" });
        Assert.DoesNotContain(panes.Panes!, p => p.Id == menu.Env[FleetDaemon.PaneVariable]);
    }

    [Fact]
    public async Task A_pane_whose_process_exits_is_removed()
    {
        var control = await ControlAsync();
        var pane = await SpawnAsync(control, "techweb", "claude");

        _panes.ByProgram("claude")!.Exit();

        await Eventually(async () =>
            (await control.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes!.All(p => p.Id != pane));
    }

    [Fact]
    public async Task Closing_a_workspace_kills_every_pane_in_it_and_nothing_else()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "a");
        await SpawnAsync(control, "techweb", "b");
        await SpawnAsync(control, "fleet", "c");

        var closed = await control.RequestAsync(new ControlRequest { Op = "close-workspace", Workspace = "techweb" });

        Assert.True(closed.Ok, closed.Error);
        Assert.True(_panes.ByProgram("a")!.IsDisposed);
        Assert.True(_panes.ByProgram("b")!.IsDisposed);
        Assert.False(_panes.ByProgram("c")!.IsDisposed);
    }

    [Fact]
    public async Task A_client_speaking_another_protocol_version_is_refused_with_a_reason()
    {
        await using var stream = await _endpoint.ConnectAsync(TimeSpan.FromSeconds(5));
        using var wire = new Wire(stream);
        await wire.SendAsync(MessageType.Hello, new Hello { Version = 999 }, WireJsonContext.Default.Hello);

        var reply = await wire.ReceiveAsync();

        Assert.Equal(MessageType.Error, reply!.Value.Type);
        Assert.Contains("999", Wire.Read(reply.Value.Payload, WireJsonContext.Default.ErrorMessage).Message);
    }

    [Fact]
    public async Task A_click_focuses_the_pane_under_it_and_reaches_it_in_its_own_coordinates()
    {
        var control = await ControlAsync();
        var left = await SpawnAsync(control, "techweb", "left");
        await control.RequestAsync(new ControlRequest { Op = "split", Pane = left, Direction = "right", Args = ["right"] });
        var client = await AttachAsync(cols: 41, rows: 11, workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendMouseAsync(3, 3, MouseButtons.Left, MouseActions.Press, held: true);
        await client.SendMouseAsync(3, 3, MouseButtons.Left, MouseActions.Release);
        await client.SendKeyAsync("k");

        await Eventually(() => _panes.ByProgram("left")!.Written == "<mouse b1 a0 3,2><mouse b1 a1 3,2>k");
        Assert.DoesNotContain("<mouse", _panes.ByProgram("right")!.Written);
    }

    [Fact]
    public async Task A_drag_that_leaves_the_pane_stays_with_the_pane_it_started_in()
    {
        var control = await ControlAsync();
        var left = await SpawnAsync(control, "techweb", "left");
        await control.RequestAsync(new ControlRequest { Op = "split", Pane = left, Direction = "right", Args = ["right"] });
        var client = await AttachAsync(cols: 41, rows: 11, workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendMouseAsync(3, 3, MouseButtons.Left, MouseActions.Press, held: true);
        await client.SendMouseAsync(35, 3, MouseButtons.Left, MouseActions.Motion, held: true);
        await client.SendMouseAsync(35, 3, MouseButtons.Left, MouseActions.Release);

        await Eventually(() => _panes.ByProgram("left")!.Written.EndsWith(" a1 19,2>", StringComparison.Ordinal));
        Assert.Equal(string.Empty, _panes.ByProgram("right")!.Written);
    }

    [Fact]
    public async Task The_wheel_goes_to_the_pane_under_the_pointer_without_moving_focus()
    {
        var control = await ControlAsync();
        var left = await SpawnAsync(control, "techweb", "left");
        await control.RequestAsync(new ControlRequest { Op = "split", Pane = left, Direction = "right", Args = ["right"] });
        var client = await AttachAsync(cols: 41, rows: 11, workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendMouseAsync(2, 3, MouseButtons.WheelDown, MouseActions.Press);
        await client.SendKeyAsync("k");

        await Eventually(() => _panes.ByProgram("left")!.Written == "<mouse b5 a0 2,2>"
                                && _panes.ByProgram("right")!.Written == "k");
    }

    [Fact]
    public async Task Clicking_a_tab_in_the_status_bar_shows_that_tab()
    {
        var control = await ControlAsync();
        var first = await SpawnAsync(control, "techweb", "first");
        await control.RequestAsync(new ControlRequest { Op = "title", Pane = first, Text = "one" });
        var second = await SpawnAsync(control, "techweb", "second");
        await control.RequestAsync(new ControlRequest { Op = "title", Pane = second, Text = "two" });
        var client = await AttachAsync(cols: 40, rows: 6, workspace: "techweb");
        await client.WaitForFramesAsync(1);

        var firstTab = "  techweb  ".Length + 1;
        await client.SendMouseAsync(firstTab, 0, MouseButtons.Left, MouseActions.Press, held: true);
        await client.SendMouseAsync(firstTab, 0, MouseButtons.Left, MouseActions.Release);
        await client.SendKeyAsync("k");

        await Eventually(() => _panes.ByProgram("first")!.Written == "k");
    }

    [Fact]
    public async Task The_window_title_follows_the_focused_panes_title_and_the_workspace()
    {
        var control = await ControlAsync();
        var pane = await SpawnAsync(control, "techweb", "nvim");
        await SpawnAsync(control, "fleet", "claude");
        var client = await AttachAsync(workspace: "techweb");
        await Eventually(() => client.LastTitle == "techweb · fleet");

        _panes.ByProgram("nvim")!.Terminal.SetTitle("README.md - NVIM");

        await Eventually(() => client.LastTitle == "README.md - NVIM · techweb");
        var listed = await control.RequestAsync(new ControlRequest { Op = "list-panes" });
        Assert.Equal("README.md - NVIM", listed.Panes!.Single(p => p.Id == pane).PaneTitle);

        await client.RequestAsync(new ControlRequest { Op = "show", Workspace = "fleet" });
        await Eventually(() => client.LastTitle == "fleet · fleet");
    }

    [Fact]
    public async Task A_copy_in_a_pane_reaches_the_clipboard_of_the_client_showing_it_only()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "nvim");
        await SpawnAsync(control, "fleet", "claude");
        var laptop = await AttachAsync(workspace: "techweb");
        var desktop = await AttachAsync(workspace: "fleet");
        await laptop.WaitForFramesAsync(1);
        await desktop.WaitForFramesAsync(1);

        _panes.ByProgram("nvim")!.Terminal.Copy("yanked text");

        await Eventually(() => laptop.Effects.Any(e => e.Kind == HostEffects.Clipboard && e.Value == "yanked text"));
        await Task.Delay(100);
        Assert.DoesNotContain(desktop.Effects, e => e.Kind == HostEffects.Clipboard);
    }

    [Fact]
    public async Task Fleets_own_handlers_switch_projects_through_the_embedded_driver()
    {
        using var mux = new EmbeddedDriver(_endpoint);
        var techweb = new Project("techweb", ".");
        var fleet = new Project("fleet", ".");
        Assert.True((await new OpenProjectHandler(mux)
            .HandleAsync(new OpenProjectCommand(techweb, AgentHarness.Claude, "fleet", null))).Succeeded);
        var opened = await new OpenProjectHandler(mux)
            .HandleAsync(new OpenProjectCommand(fleet, AgentHarness.Claude, "fleet", null));
        Assert.True(opened.Succeeded);
        _panes.Started.Single(p => p.Env[FleetDaemon.PaneVariable] == opened.Value.HarnessPane.Value).Emit("fleet-prompt");

        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);
        using var inClient = new EmbeddedDriver(_endpoint, client: client.Id);
        var started = _panes.Started.Count;

        var switched = await new SwitchProjectHandler(inClient).HandleAsync("fleet");

        Assert.True(switched.Succeeded, switched.Error);
        await client.WaitForAsync("fleet-prompt");
        Assert.Equal(started, _panes.Started.Count);
        Assert.Equal(0, _panes.Disposed);

        var located = await new LocateProjectHandler(inClient).HandleAsync([techweb, fleet]);
        Assert.True(located["techweb"].Open);
        Assert.False(located["techweb"].ShownHere);
        Assert.True(located["fleet"].ShownHere);
    }

    [Fact]
    public async Task Without_an_attached_client_the_driver_refuses_to_show_rather_than_guess()
    {
        using var mux = new EmbeddedDriver(_endpoint);
        await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", NewWindow = true, Args = ["a"] });

        if (Environment.GetEnvironmentVariable(FleetDaemon.ClientVariable) is null)
        {
            await Assert.ThrowsAsync<MuxUnavailableException>(() => mux.ShowWorkspaceAsync("techweb"));
        }
    }

    [Fact]
    public async Task The_driver_reports_an_unreachable_daemon_as_unavailable()
    {
        using var mux = new EmbeddedDriver(new Endpoint(
            OperatingSystem.IsWindows() ? "fleet-test-nobody-home" : Path.Combine(Path.GetTempPath(), "fleet-nobody.sock")));

        Assert.False(await mux.IsAvailableAsync());
        await Assert.ThrowsAsync<MuxUnavailableException>(() => mux.ListPanesAsync());
    }

    [Fact]
    public async Task A_new_float_takes_the_keys_until_the_floats_are_hidden()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "tile");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendCommandAsync("float-new");
        await client.SendKeyAsync("k");
        await client.SendCommandAsync("float-toggle");
        await client.SendKeyAsync("j");

        await Eventually(() => _panes.Started.Count == 2
                                && _panes.Started.Last().Written == "k"
                                && _panes.ByProgram("tile")!.Written == "j");
        Assert.True(_panes.Started.Last().Env.ContainsKey(FleetDaemon.ClientVariable));
    }

    [Fact]
    public async Task Dragging_a_floats_border_moves_it_without_the_pane_seeing_the_drag()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "tile");
        var client = await AttachAsync(cols: 60, rows: 12, workspace: "techweb");
        await client.WaitForFramesAsync(1);
        var spawned = await control.RequestAsync(new ControlRequest { Op = "spawn-float", Session = "techweb", Args = ["box"] });
        Assert.True(spawned.Ok, spawned.Error);

        await client.SendMouseAsync(20, 3, MouseButtons.Left, MouseActions.Press, held: true);
        await client.SendMouseAsync(25, 5, MouseButtons.Left, MouseActions.Motion, held: true);
        await client.SendMouseAsync(25, 5, MouseButtons.Left, MouseActions.Release);
        await client.SendMouseAsync(18, 6, MouseButtons.Left, MouseActions.Press, held: true);

        await Eventually(() => _panes.ByProgram("box")!.Written == "<mouse b1 a0 0,0>");
        var listed = await control.RequestAsync(new ControlRequest { Op = "list-panes" });
        Assert.Equal("float", listed.Panes!.Single(p => p.Id == spawned.Pane).Tab);
    }

    [Fact]
    public async Task Float_move_and_size_commands_nudge_the_focused_float()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "tile");
        var client = await AttachAsync(cols: 60, rows: 12, workspace: "techweb");
        await client.WaitForFramesAsync(1);
        await control.RequestAsync(new ControlRequest { Op = "spawn-float", Session = "techweb", Args = ["box"] });

        await client.SendCommandAsync("float-move", "down");
        await client.SendCommandAsync("float-size", "right");
        await client.SendMouseAsync(13, 5, MouseButtons.Left, MouseActions.Press, held: true);

        await Eventually(() => _panes.ByProgram("box")!.Written == "<mouse b1 a0 0,0>");
        await Eventually(() => _panes.ByProgram("box")!.Size == (35, 4));
    }

    [Fact]
    public async Task A_repeated_key_is_encoded_once_per_repeat_for_a_pane_without_win32_input()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "shell");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendKeyAsync(new KeyMessage { Text = "x", Action = 1, Repeat = 3 });
        await client.SendKeyAsync(new KeyMessage { Text = "y", Action = 0, Repeat = 2 });

        await client.SendKeyAsync("z");
        await Eventually(() => _panes.ByProgram("shell")!.Written == "xxxz");
    }

    [Fact]
    public async Task A_dead_key_press_puts_nothing_into_a_pane_without_win32_input()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "shell");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendKeyAsync(new KeyMessage { Key = 0, Text = null, Action = 1, Win32 = new Win32Key { Vk = 0xDE, Down = true } });
        await client.SendKeyAsync("é");

        await Eventually(() => _panes.ByProgram("shell")!.Written == "é");
    }
    [Fact]
    public async Task A_repeated_key_reaches_a_win32_input_pane_as_one_record_with_its_count()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "conpty");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);
        _panes.ByProgram("conpty")!.Emit("\e[?9001h");
        await Task.Delay(100);

        await client.SendKeyAsync(new KeyMessage
        {
            Text = "x",
            Action = 1,
            Repeat = 3,
            Win32 = new Win32Key { Vk = 0x58, Sc = 45, Uc = 'x', Down = true, State = 0, Repeat = 3 },
        });

        await Eventually(() => _panes.ByProgram("conpty")!.Written == "\e[88;45;120;1;0;3_");
    }

    [Fact]
    public async Task A_float_over_an_agent_pane_opens_in_its_workspace_and_takes_the_keys()
    {
        var control = await ControlAsync();
        var agent = await SpawnAsync(control, "techweb", "claude");
        await SpawnAsync(control, "fleet", "other");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        var spawned = await control.RequestAsync(new ControlRequest { Op = "spawn-float", Pane = agent, Args = ["approve"] });
        await client.SendKeyAsync("y");

        await Eventually(() => _panes.ByProgram("approve")?.Written == "y");
        var listed = (await control.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes!.Single(p => p.Id == spawned.Pane);
        Assert.Equal(("techweb", "float"), (listed.Session, listed.Tab));
    }
    [Fact]
    public async Task The_wheel_scrolls_history_when_the_program_did_not_ask_for_the_mouse_and_typing_snaps_back()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "shell");
        var client = await AttachAsync(cols: 30, rows: 8, workspace: "techweb");
        await client.WaitForFramesAsync(1);
        var shell = _panes.ByProgram("shell")!;
        shell.Terminal.MouseTracking = false;
        shell.Emit(string.Join('\n', Enumerable.Range(1, 40).Select(i => $"line {i}")));

        await client.SendMouseAsync(3, 2, MouseButtons.WheelUp, MouseActions.Press);

        await Eventually(() => shell.Terminal.Viewport is { AtBottom: false } v && v.Below == FleetDaemon.WheelLines);
        await client.WaitForAsync($"[{FleetDaemon.WheelLines}/");
        Assert.Equal(string.Empty, shell.Written);

        await client.SendKeyAsync("x");

        await Eventually(() => shell.Terminal.Viewport.AtBottom && shell.Written == "x");
    }

    [Fact]
    public async Task The_wheel_on_a_full_screen_program_without_mouse_becomes_arrow_keys()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "less");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);
        var less = _panes.ByProgram("less")!;
        less.Terminal.MouseTracking = false;
        less.Terminal.AltScreen = true;

        await client.SendMouseAsync(3, 2, MouseButtons.WheelDown, MouseActions.Press);

        await Eventually(() => less.Written == "<key ArrowDown><key ArrowDown><key ArrowDown>");
        Assert.True(less.Terminal.Viewport.AtBottom);
    }

    [Fact]
    public async Task Copy_mode_yanks_a_line_from_history_into_the_clipboard_of_the_client_that_copied()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "shell");
        var client = await AttachAsync(cols: 30, rows: 8, workspace: "techweb");
        await client.WaitForFramesAsync(1);
        var shell = _panes.ByProgram("shell")!;
        shell.Emit(string.Join('\n', Enumerable.Range(1, 40).Select(i => $"line {i}")));

        await client.SendCommandAsync("copy-mode");
        foreach (var step in Enumerable.Repeat("up", 10))
        {
            await client.SendCommandAsync("copy", step);
        }

        await Eventually(() => !shell.Terminal.Viewport.AtBottom);
        await client.SendCommandAsync("copy", "yank");

        await Eventually(() => client.Effects.Any(e => e.Kind == HostEffects.Clipboard && e.Value == "line 30"));
        Assert.True(shell.Terminal.Viewport.AtBottom);
        Assert.Equal(string.Empty, shell.Written);
    }
    [Fact]
    public async Task A_float_asked_for_from_a_pane_opens_in_that_panes_workspace_as_an_ordinary_float()
    {
        var control = await ControlAsync();
        var menu = await SpawnAsync(control, "techweb", "menu");
        await SpawnAsync(control, "fleet", "other");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        var spawned = await control.RequestAsync(new ControlRequest { Op = "spawn-float", Caller = menu, Args = ["yazi"] });
        await client.SendCommandAsync("float-toggle");
        await client.SendKeyAsync("m");

        var listed = (await control.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes!.Single(p => p.Id == spawned.Pane);
        Assert.Equal(("techweb", "float"), (listed.Session, listed.Tab));
        await Eventually(() => _panes.ByProgram("menu")!.Written == "m");
    }
    [Fact]
    public async Task Split_right_opens_a_shell_beside_the_focused_pane_that_then_takes_the_keys()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "claude");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendCommandAsync("split-right");
        await client.SendKeyAsync("s");

        await Eventually(() => _panes.Started.Count == 2 && _panes.Started.Last().Written == "s");
        var panes = (await control.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes!;
        Assert.Single(panes.Select(p => p.Tab).Distinct());
    }

    [Fact]
    public async Task Smart_focus_moves_between_panes_but_leaves_the_key_to_nvim()
    {
        var control = await ControlAsync();
        var left = await SpawnAsync(control, "techweb", "nvim");
        await control.RequestAsync(new ControlRequest { Op = "split", Pane = left, Direction = "right", Args = ["shell"] });
        var client = await AttachAsync(cols: 41, rows: 11, workspace: "techweb");
        await client.WaitForFramesAsync(1);
        var ctrlH = new KeyMessage { Key = (int)Fleet.Platform.Mux.Embedded.Native.Key.H, Mods = 2, Action = 1, Text = "<ctrl+h>" };

        await client.SendCommandAsync(new CommandMessage { Name = "smart-focus", Arg = "left", Key = ctrlH });
        await client.SendKeyAsync("a");
        await client.SendCommandAsync(new CommandMessage { Name = "smart-focus", Arg = "right", Key = ctrlH });
        await client.SendKeyAsync("b");

        await Eventually(() => _panes.ByProgram("nvim")!.Written == "a<ctrl+h>b");
        Assert.Equal(string.Empty, _panes.ByProgram("shell")!.Written);
    }

    [Fact]
    public async Task Shift_enter_becomes_a_newline_for_a_shell_and_a_csi_u_key_for_nvim()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "claude");
        await SpawnAsync(control, "fleet", "nvim");
        var laptop = await AttachAsync(workspace: "techweb");
        var desktop = await AttachAsync(workspace: "fleet");
        await laptop.WaitForFramesAsync(1);
        await desktop.WaitForFramesAsync(1);

        await laptop.SendCommandAsync("newline");
        await desktop.SendCommandAsync("newline");

        await Eventually(() => _panes.ByProgram("claude")!.Written == "\n" && _panes.ByProgram("nvim")!.Written == "\e[13;2u");
    }

    [Fact]
    public async Task Kill_pane_closes_the_focused_pane_only()
    {
        var control = await ControlAsync();
        var left = await SpawnAsync(control, "techweb", "left");
        await control.RequestAsync(new ControlRequest { Op = "split", Pane = left, Direction = "right", Args = ["right"] });
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendCommandAsync("kill-pane");

        await Eventually(() => _panes.ByProgram("right")!.IsDisposed);
        Assert.False(_panes.ByProgram("left")!.IsDisposed);
    }

    [Fact]
    public async Task The_prefix_badge_draws_the_which_key_box()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "claude");
        var client = await AttachAsync(cols: 60, rows: 12, workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendBadgeAsync(new BadgeMessage
        {
            Text = "ctrl+s",
            Keys = [new WhichKeyEntry { Key = "%", Label = "split right" }, new WhichKeyEntry { Key = "z", Label = "zoom" }],
        });

        await client.WaitForAsync("split right");
        await client.WaitForAsync("zoom");
    }

    [Fact]
    public async Task A_pane_can_move_focus_from_itself_like_wezterm_cli()
    {
        var control = await ControlAsync();
        var left = await SpawnAsync(control, "techweb", "left");
        await control.RequestAsync(new ControlRequest { Op = "split", Pane = left, Direction = "right", Args = ["right"] });
        var client = await AttachAsync(cols: 41, rows: 11, workspace: "techweb");
        await client.WaitForFramesAsync(1);

        var moved = await control.RequestAsync(new ControlRequest { Op = "focus-from", Caller = left, Direction = "Left" });
        Assert.True(moved.Ok, moved.Error);
        await control.RequestAsync(new ControlRequest { Op = "focus-from", Pane = _panes.ByProgram("right")!.Env[FleetDaemon.PaneVariable], Direction = "Left" });
        await client.SendKeyAsync("x");

        await Eventually(() => _panes.ByProgram("left")!.Written == "x");
        Assert.Equal("fleet", _panes.ByProgram("left")!.Env["WEZTERM_EXECUTABLE"]);
    }
    [Fact]
    public async Task A_float_can_be_tiled_and_floated_again()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "tile");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);
        var box = (await control.RequestAsync(new ControlRequest { Op = "spawn-float", Session = "techweb", Args = ["box"] })).Pane;

        async Task<string?> TabOfBox() =>
            (await control.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes!.Single(p => p.Id == box).Tab;

        await client.SendCommandAsync("float-embed");
        await Eventually(async () => await TabOfBox() != "float");

        await client.SendCommandAsync("float-embed");
        await Eventually(async () => await TabOfBox() == "float");
    }

    private static async Task Eventually(Func<bool> condition) => await Eventually(() => Task.FromResult(condition()));

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("the condition never became true");
    }

    private sealed class TestClient : IAsyncDisposable
    {
        private readonly Wire _wire;
        private readonly ConcurrentQueue<string> _frames = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<ControlResponse>> _pending = new();
        private readonly Task _reader;
        private int _nextId;

        private TestClient(Wire wire, string id)
        {
            _wire = wire;
            Id = id;
            _reader = Task.Run(ReadAsync);
        }

        public string Id { get; }

        public ConcurrentQueue<HostEffect> Effects { get; } = new();

        public string? LastTitle => Effects.LastOrDefault(e => e.Kind == HostEffects.Title)?.Value;

        public int FrameCount => _frames.Count;

        public string AllText => string.Concat(_frames);

        public static async Task<TestClient> ConnectAsync(Endpoint endpoint, string role, int cols, int rows, string? workspace)
        {
            var wire = new Wire(await endpoint.ConnectAsync(TimeSpan.FromSeconds(5)));
            await wire.SendAsync(
                MessageType.Hello,
                new Hello { Version = Wire.Version, Role = role, Cols = cols, Rows = rows, Workspace = workspace, Os = "test" },
                WireJsonContext.Default.Hello);

            var welcome = await wire.ReceiveAsync();
            Assert.Equal(MessageType.Welcome, welcome!.Value.Type);
            return new TestClient(wire, Wire.Read(welcome.Value.Payload, WireJsonContext.Default.Welcome).Client);
        }

        public async Task<ControlResponse> RequestAsync(ControlRequest request)
        {
            request.Id = Interlocked.Increment(ref _nextId);
            var reply = new TaskCompletionSource<ControlResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[request.Id] = reply;
            await _wire.SendAsync(MessageType.Request, request, WireJsonContext.Default.ControlRequest);
            return await reply.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public Task SendCommandAsync(CommandMessage command) =>
            _wire.SendAsync(MessageType.Command, command, WireJsonContext.Default.CommandMessage);

        public Task SendBadgeAsync(BadgeMessage badge) =>
            _wire.SendAsync(MessageType.Badge, badge, WireJsonContext.Default.BadgeMessage);
        public Task SendKeyAsync(KeyMessage key) =>
            _wire.SendAsync(MessageType.Key, key, WireJsonContext.Default.KeyMessage);

        public Task SendKeyAsync(string text) =>
            _wire.SendAsync(MessageType.Key, new KeyMessage { Text = text, Action = 1 }, WireJsonContext.Default.KeyMessage);

        public Task SendMouseAsync(int x, int y, int button, int action, bool held = false) =>
            _wire.SendAsync(
                MessageType.Mouse,
                new MouseMessage { X = x, Y = y, Button = button, Action = action, Held = held },
                WireJsonContext.Default.MouseMessage);

        public Task SendCommandAsync(string name, string? arg = null) =>
            _wire.SendAsync(MessageType.Command, new CommandMessage { Name = name, Arg = arg }, WireJsonContext.Default.CommandMessage);

        public async Task WaitForAsync(string text)
        {
            await Eventually(() => AllText.Contains(text, StringComparison.Ordinal));
        }

        public async Task WaitForFramesAsync(int count)
        {
            await Eventually(() => FrameCount >= count);
        }

        public async ValueTask DisposeAsync()
        {
            _wire.Dispose();
            try
            {
                await _reader;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }

        private async Task ReadAsync()
        {
            try
            {
                while (await _wire.ReceiveAsync() is { } message)
                {
                    if (message.Type == MessageType.HostEffect)
                    {
                        Effects.Enqueue(Wire.Read(message.Payload, WireJsonContext.Default.HostEffect));
                    }
                    else if (message.Type == MessageType.Frame)
                    {
                        _frames.Enqueue(Encoding.UTF8.GetString(Wire.ReadFrame(message.Payload).Bytes));
                    }
                    else if (message.Type == MessageType.Response)
                    {
                        var response = Wire.Read(message.Payload, WireJsonContext.Default.ControlResponse);
                        if (_pending.TryRemove(response.Id, out var waiter))
                        {
                            waiter.SetResult(response);
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or EndOfStreamException or OperationCanceledException)
            {
            }
        }
    }
}
