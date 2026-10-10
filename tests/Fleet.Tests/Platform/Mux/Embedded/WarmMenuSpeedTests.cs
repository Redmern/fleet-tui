using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;

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

    private static Task<ControlResponse> WaitForOpen(DaemonTests.TestClient control, FakePanes.FakePty menu) =>
        control.RequestAsync(new ControlRequest { Op = FleetDaemon.MenuWaitOp, Caller = menu.Env[FleetDaemon.PaneVariable] });

    [Fact]
    public async Task A_menu_opened_with_an_action_switches_a_waiting_warm_menu_to_it()
    {
        var (control, client, warm) = await WarmAsync();
        await Task.Delay(FleetDaemon.RevealAnyway - FleetDaemon.RevealAfterFit + TimeSpan.FromMilliseconds(200));
        var waiting = WaitForOpen(control, warm);
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);

        await client.SendCommandAsync("menu", "switch-project");

        var opened = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(opened.Ok, opened.Error);
        Assert.Equal("switch-project", opened.Text);
        await Task.Delay(200);
        warm.Emit("\u001b[2J\u001b[HACTION-SCREEN\nprojects\n");
        await client.WaitForAsync("ACTION-SCREEN");
        Assert.DoesNotContain(client.Frames.TakeWhile(f => !f.Contains("ACTION-SCREEN")), f => f.Contains("WARM-MENU"));
        Assert.Equal(1, Menus);
    }

    [Fact]
    public async Task A_plain_menu_open_tells_a_waiting_warm_menu_there_is_no_action()
    {
        var (control, client, warm) = await WarmAsync();
        var waiting = WaitForOpen(control, warm);
        await Task.Delay(100);

        await client.SendCommandAsync("menu");

        var opened = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(opened.Ok, opened.Error);
        Assert.Null(opened.Text);
        await client.WaitForAsync("WARM-MENU");
    }

    [Fact]
    public async Task A_menu_opened_with_an_action_starts_a_cold_one_when_no_warm_menu_waits()
    {
        var (_, client, warm) = await WarmAsync();

        await client.SendCommandAsync("menu", "switch-project");

        await Eventually(() => Menus == 2);
        var cold = _panes.Started.Last(p => p.Program == "fleet");
        Assert.Equal(["menu", "--project", "techweb", "--action", "switch-project"], cold.Args);
        Assert.False(warm.IsDisposed);
    }

    [Fact]
    public async Task The_driver_waits_until_its_warm_menu_is_opened_and_returns_the_action()
    {
        var (_, client, warm) = await WarmAsync();
        using var driver = new EmbeddedDriver(_endpoint);
        var waiting = driver.WaitForMenuOpenAsync(new PaneId(warm.Env[FleetDaemon.PaneVariable]));
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);

        await client.SendCommandAsync("menu", "notifications");

        Assert.Equal("notifications", await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task The_driver_reports_a_menu_that_is_not_parked()
    {
        var (_, client, warm) = await WarmAsync();
        await client.SendCommandAsync("menu");
        await client.WaitForAsync("WARM-MENU");
        using var driver = new EmbeddedDriver(_endpoint);

        await Assert.ThrowsAsync<MuxUnavailableException>(
            () => driver.WaitForMenuOpenAsync(new PaneId(warm.Env[FleetDaemon.PaneVariable])).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Only_a_parked_warm_menu_may_wait_to_be_opened()
    {
        var (control, client, warm) = await WarmAsync();
        await client.SendCommandAsync("menu");
        await client.WaitForAsync("WARM-MENU");

        var answer = await WaitForOpen(control, warm).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(answer.Ok);
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
