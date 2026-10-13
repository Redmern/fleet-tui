using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
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

    private static async Task DragAsync(TestClient client, (int X, int Y) from, (int X, int Y) to, int mods = 0)
    {
        await client.SendMouseAsync(from.X, from.Y, MouseButtons.Left, MouseActions.Press, held: true, mods: mods);
        await client.SendMouseAsync(to.X, to.Y, MouseButtons.Left, MouseActions.Motion, held: true, mods: mods);
        await client.SendMouseAsync(to.X, to.Y, MouseButtons.Left, MouseActions.Release, mods: mods);
    }

    private async Task<(TestClient Client, FakePanes.FakePty Shell)> ShellAsync(bool tracking)
    {
        var control = await ConnectAsync(ClientRoles.Control, 0, 0, null);
        var spawned = await control.RequestAsync(new ControlRequest { Op = "spawn", Session = "techweb", NewWindow = true, Cwd = ".", Args = ["shell"] });
        Assert.True(spawned.Ok, spawned.Error);
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
