using System.Collections.Concurrent;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

// The pause after the fit lets fleetd take its baseline before the menu draws.
// The local quiet window is stretched past the reveal-anyway fallback (1.3 s after a fit), so a
// local menu can only be revealed "after waiting" and a bridged one "after drawing"; the tests
// read that from fleetd's log instead of timing the reveal.
public sealed class RevealQuietTests : IAsyncLifetime
{
    private static readonly TimeSpan LocalQuiet = TimeSpan.FromSeconds(30);

    private readonly FakePanes _panes = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private readonly ConcurrentQueue<string> _log = new();
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
            RevealWhenQuiet = LocalQuiet,
            Log = _log.Enqueue,
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

    private async Task<DaemonTests.TestClient> ConnectAsync(string role, string? workspace = null, bool bridged = false)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(_endpoint, role, 100, 30, workspace, bridged);
        _clients.Add(client);
        return client;
    }

    private async Task<(DaemonTests.TestClient Client, string Menu)> OpenDrawnMenuAsync(bool bridged)
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

        var client = await ConnectAsync(ClientRoles.Attach, "techweb", bridged);
        await client.WaitForFramesAsync(1);
        await client.SendCommandAsync("menu");
        await Eventually(() => _panes.ByProgram("fleet") is not null);

        var menu = _panes.ByProgram("fleet")!;
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "fit",
            Caller = menu.Env[FleetDaemon.PaneVariable],
            Cols = 40,
            Rows = 8,
        })).Ok);
        await Task.Delay(100);
        menu.Emit("THE-MENU\nitems\n");
        return (client, menu.Env[FleetDaemon.PaneVariable]);
    }

    [Fact]
    public void The_local_quiet_window_stays_60_ms_and_the_remote_one_is_shorter()
    {
        var options = new DaemonOptions { Endpoint = _endpoint, Pty = _panes.NewPty, Terminal = _panes.NewTerminal };

        Assert.Equal(TimeSpan.FromMilliseconds(60), options.RevealWhenQuiet);
        Assert.Equal(TimeSpan.FromMilliseconds(15), options.RevealWhenQuietRemote);
    }

    [Fact]
    public async Task A_bridged_client_sees_a_drawn_menu_after_the_remote_quiet_window()
    {
        var (client, menu) = await OpenDrawnMenuAsync(bridged: true);

        await client.WaitForAsync("THE-MENU");

        Assert.Contains($"{menu} revealed after drawing", _log);
    }

    [Fact]
    public async Task A_local_client_still_waits_the_local_quiet_window()
    {
        var (client, menu) = await OpenDrawnMenuAsync(bridged: false);

        await client.WaitForAsync("THE-MENU");

        Assert.Contains($"{menu} revealed after waiting", _log);
        Assert.DoesNotContain($"{menu} revealed after drawing", _log);
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
