using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Protocol;
using static Fleet.Tests.Platform.Mux.Embedded.DaemonTests;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class MouseSelectTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly List<TestClient> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private Endpoint _endpoint = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        _running = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = "fleet",
        }).RunAsync(_stop.Token);
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

    [Fact]
    public async Task Dragging_over_a_pane_that_did_not_ask_for_the_mouse_copies_the_selection_on_release()
    {
        var (client, shell) = await ShellAsync(tracking: false);

        await DragAsync(client, (0, 1), (4, 2));

        await Eventually(() => client.Effects.Any(e => e.Kind == HostEffects.Clipboard && e.Value == "alpha beta\ngamma"));
        Assert.Equal(string.Empty, shell.Written);
    }

    [Fact]
    public async Task Dragging_over_a_pane_that_asked_for_the_mouse_goes_to_the_pane_and_copies_nothing()
    {
        var (client, shell) = await ShellAsync(tracking: true);

        await DragAsync(client, (0, 1), (4, 2));

        await Eventually(() => shell.Written.EndsWith("a1 4,1>", StringComparison.Ordinal));
        Assert.StartsWith("<mouse b1 a0 0,0>", shell.Written, StringComparison.Ordinal);
        Assert.DoesNotContain(client.Effects, e => e.Kind == HostEffects.Clipboard);
    }

    [Fact]
    public async Task Holding_shift_selects_even_in_a_pane_that_asked_for_the_mouse()
    {
        var (client, shell) = await ShellAsync(tracking: true);

        await DragAsync(client, (6, 1), (2, 2), mods: 1);

        await Eventually(() => client.Effects.Any(e => e.Kind == HostEffects.Clipboard && e.Value == "beta\ngam"));
        Assert.Equal(string.Empty, shell.Written);
    }

    [Fact]
    public async Task A_click_without_a_drag_copies_nothing_and_leaves_nothing_selected()
    {
        var (client, shell) = await ShellAsync(tracking: false);

        await client.SendMouseAsync(3, 1, MouseButtons.Left, MouseActions.Press, held: true);
        await client.SendMouseAsync(3, 1, MouseButtons.Left, MouseActions.Release);
        await client.SendCommandAsync(CopyMode.Command, CopyMode.Selection);

        await client.WaitForAsync(CopyMode.NothingSelected);
        Assert.DoesNotContain(client.Effects, e => e.Kind == HostEffects.Clipboard);
        Assert.Equal(string.Empty, shell.Written);
    }

    [Fact]
    public async Task The_selection_stays_after_release_so_the_copy_command_copies_it_again()
    {
        var (client, _) = await ShellAsync(tracking: false);

        await DragAsync(client, (0, 1), (4, 1));
        await Eventually(() => client.Effects.Count(e => e.Kind == HostEffects.Clipboard) == 1);
        await client.SendCommandAsync(CopyMode.Command, CopyMode.Selection);

        await Eventually(() => client.Effects.Count(e => e.Kind == HostEffects.Clipboard && e.Value == "alpha") == 2);
    }

    [Fact]
    public async Task A_new_click_clears_the_selection()
    {
        var (client, _) = await ShellAsync(tracking: false);

        await DragAsync(client, (0, 1), (4, 1));
        await Eventually(() => client.Effects.Any(e => e.Kind == HostEffects.Clipboard));
        await client.SendMouseAsync(8, 2, MouseButtons.Left, MouseActions.Press, held: true);
        await client.SendMouseAsync(8, 2, MouseButtons.Left, MouseActions.Release);
        await client.SendCommandAsync(CopyMode.Command, CopyMode.Selection);

        await client.WaitForAsync(CopyMode.NothingSelected);
        Assert.Single(client.Effects, e => e.Kind == HostEffects.Clipboard);
    }

    [Fact]
    public async Task Dragging_a_divider_between_panes_that_did_not_ask_for_the_mouse_still_resizes_and_copies_nothing()
    {
        var control = await ConnectAsync(ClientRoles.Control, 0, 0, null);
        var left = await SpawnAsync(control, "left");
        await control.RequestAsync(new ControlRequest { Op = "split", Pane = left, Direction = "right", Args = ["right"] });
        var client = await ConnectAsync(ClientRoles.Attach, 41, 11, "techweb");
        await client.WaitForFramesAsync(1);
        var leftPty = _panes.ByProgram("left")!;
        var rightPty = _panes.ByProgram("right")!;
        leftPty.Terminal.MouseTracking = false;
        rightPty.Terminal.MouseTracking = false;
        await Eventually(() => leftPty.Size.Cols == 20);

        await DragAsync(client, (20, 3), (30, 3));

        await Eventually(() => leftPty.Size.Cols == 30);
        Assert.DoesNotContain(client.Effects, e => e.Kind == HostEffects.Clipboard);
        Assert.Equal(string.Empty, leftPty.Written + rightPty.Written);
    }

    [Fact]
    public async Task Dragging_a_float_over_a_pane_that_did_not_ask_for_the_mouse_still_moves_it_and_copies_nothing()
    {
        var control = await ConnectAsync(ClientRoles.Control, 0, 0, null);
        await SpawnAsync(control, "tile");
        var client = await ConnectAsync(ClientRoles.Attach, 60, 12, "techweb");
        await client.WaitForFramesAsync(1);
        _panes.ByProgram("tile")!.Terminal.MouseTracking = false;
        var spawned = await control.RequestAsync(new ControlRequest { Op = "spawn-float", Session = "techweb", Args = ["box"] });
        Assert.True(spawned.Ok, spawned.Error);

        await DragAsync(client, (20, 3), (25, 5));
        await client.SendMouseAsync(18, 6, MouseButtons.Left, MouseActions.Press, held: true);

        await Eventually(() => _panes.ByProgram("box")!.Written == "<mouse b1 a0 0,0>");
        Assert.DoesNotContain(client.Effects, e => e.Kind == HostEffects.Clipboard);
        Assert.Equal(string.Empty, _panes.ByProgram("tile")!.Written);
    }

    private static async Task DragAsync(TestClient client, (int X, int Y) from, (int X, int Y) to, int mods = 0)
    {
        await client.SendMouseAsync(from.X, from.Y, MouseButtons.Left, MouseActions.Press, held: true, mods: mods);
        await client.SendMouseAsync(to.X, to.Y, MouseButtons.Left, MouseActions.Motion, held: true, mods: mods);
        await client.SendMouseAsync(to.X, to.Y, MouseButtons.Left, MouseActions.Release, mods: mods);
    }

    private static async Task<string> SpawnAsync(TestClient control, string program)
    {
        var spawned = await control.RequestAsync(new ControlRequest { Op = "spawn", Session = "techweb", NewWindow = true, Cwd = ".", Args = [program] });
        Assert.True(spawned.Ok, spawned.Error);
        return spawned.Pane!;
    }

    private async Task<(TestClient Client, FakePanes.FakePty Shell)> ShellAsync(bool tracking)
    {
        var control = await ConnectAsync(ClientRoles.Control, 0, 0, null);
        await SpawnAsync(control, "shell");
        var client = await ConnectAsync(ClientRoles.Attach, 30, 8, "techweb");
        await client.WaitForFramesAsync(1);
        var shell = _panes.ByProgram("shell")!;
        shell.Terminal.MouseTracking = tracking;
        shell.Emit("alpha beta\ngamma delta");
        await client.WaitForAsync("gamma");
        return (client, shell);
    }

    private async Task<TestClient> ConnectAsync(string role, int cols, int rows, string? workspace)
    {
        var client = await TestClient.ConnectAsync(_endpoint, role, cols, rows, workspace);
        _clients.Add(client);
        return client;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }
}
