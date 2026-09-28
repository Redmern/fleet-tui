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
    public async Task The_menu_opens_as_an_overlay_that_knows_which_client_opened_it()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb", "claude");
        var client = await AttachAsync(workspace: "techweb");
        await client.WaitForFramesAsync(1);

        await client.SendCommandAsync("menu");

        await Eventually(() => _panes.ByProgram("fleet") is not null);
        var menu = _panes.ByProgram("fleet")!;
        Assert.Equal(["menu", "--project", "techweb"], menu.Args);
        Assert.Equal(client.Id, menu.Env[FleetDaemon.ClientVariable]);
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

        await client.SendMouseAsync(3, 2, MouseButtons.Left, MouseActions.Press, held: true);
        await client.SendMouseAsync(3, 2, MouseButtons.Left, MouseActions.Release);
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

        await client.SendMouseAsync(3, 2, MouseButtons.Left, MouseActions.Press, held: true);
        await client.SendMouseAsync(35, 2, MouseButtons.Left, MouseActions.Motion, held: true);
        await client.SendMouseAsync(35, 2, MouseButtons.Left, MouseActions.Release);

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

        await client.SendMouseAsync(2, 2, MouseButtons.WheelDown, MouseActions.Press);
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

        await client.SendMouseAsync(" techweb ".Length + 2, 5, MouseButtons.Left, MouseActions.Press, held: true);
        await client.SendMouseAsync(" techweb ".Length + 2, 5, MouseButtons.Left, MouseActions.Release);
        await client.SendKeyAsync("k");

        await Eventually(() => _panes.ByProgram("first")!.Written == "k");
    }

    [Fact]
    public async Task Fleets_own_handlers_switch_projects_through_the_embedded_driver()
    {
        using var mux = new EmbeddedDriver(_endpoint);
        var techweb = new Project("techweb", ".");
        var fleet = new Project("fleet", ".");
        Assert.True((await new OpenProjectHandler(mux)
            .HandleAsync(new OpenProjectCommand(techweb, "claude-techweb", "fleet", null))).Succeeded);
        Assert.True((await new OpenProjectHandler(mux)
            .HandleAsync(new OpenProjectCommand(fleet, "claude-fleet", "fleet", null))).Succeeded);
        _panes.ByProgram("claude-fleet")!.Emit("fleet-prompt");

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

        public Task SendKeyAsync(string text) =>
            _wire.SendAsync(MessageType.Key, new KeyMessage { Text = text, Action = 1 }, WireJsonContext.Default.KeyMessage);

        public Task SendMouseAsync(int x, int y, int button, int action, bool held = false) =>
            _wire.SendAsync(
                MessageType.Mouse,
                new MouseMessage { X = x, Y = y, Button = button, Action = action, Held = held },
                WireJsonContext.Default.MouseMessage);

        public Task SendCommandAsync(string name) =>
            _wire.SendAsync(MessageType.Command, new CommandMessage { Name = name }, WireJsonContext.Default.CommandMessage);

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
                    if (message.Type == MessageType.Frame)
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
