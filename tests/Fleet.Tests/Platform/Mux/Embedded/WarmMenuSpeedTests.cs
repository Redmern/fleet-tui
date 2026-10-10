using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

// Warm menus on a frozen SimulatedLinkClock: time only moves when a test advances it, so a
// warm menu that starts without the clock moving proves it skipped WarmAgainAfter.
public sealed class WarmMenuSpeedTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly SimulatedLinkClock _clock = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private Endpoint _endpoint = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = "fleet",
            WarmMenus = true,
            Clock = _clock,
        });
        _running = daemon.RunAsync(_stop.Token);
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

    private async Task<DaemonTests.TestClient> ConnectAsync(string role, string? workspace = null)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(_endpoint, role, 100, 30, workspace);
        _clients.Add(client);
        return client;
    }

    private int Menus => _panes.Started.Count(p => p.Program == "fleet");

    private FakePanes.FakePty[] Live => [.. _panes.Started.Where(p => p.Program == "fleet" && !p.IsDisposed)];

    private async Task<(DaemonTests.TestClient Control, DaemonTests.TestClient Client, FakePanes.FakePty Warm)> WarmAsync()
    {
        var control = await ConnectAsync(ClientRoles.Control);
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = "techweb",
            NewWindow = true,
            Cwd = ".",
            Args = ["claude"],
        })).Ok);

        var client = await ConnectAsync(ClientRoles.Attach, "techweb");
        await client.WaitForFramesAsync(1);
        await Eventually(() => Menus == 1);

        var warm = _panes.ByProgram("fleet")!;
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "fit",
            Caller = warm.Env[FleetDaemon.PaneVariable],
            Cols = 40,
            Rows = 8,
        })).Ok);
        warm.Emit("WARM-MENU\nitems\n");
        return (control, client, warm);
    }

    [Fact]
    public async Task Closing_a_shown_menu_starts_the_next_warm_menu_without_waiting()
    {
        var (_, client, warm) = await WarmAsync();
        await client.SendCommandAsync("menu");
        await client.WaitForAsync("WARM-MENU");

        warm.Exit();

        await Eventually(() => Menus == 2);
    }

    [Fact]
    public async Task A_warm_menu_that_dies_while_parked_still_waits_before_the_next()
    {
        var (_, _, warm) = await WarmAsync();

        warm.Exit();
        await Eventually(() => Live.Length == 0);
        await Task.Delay(200);
        Assert.Equal(1, Menus);

        _clock.Advance(TimeSpan.FromSeconds(3));
        await Eventually(() => Menus == 2);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }
}
