using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Remotes.Models;
using Xunit.Abstractions;

namespace Fleet.Tests.Platform.Mux.Embedded;

// AC-9 end to end: a home fleetd linked to a far fleetd with warm menus over a simulated
// 100 ms round-trip link. The menu is opened through the real "menu" command, and the home
// fleetd's own menu timing (FLEET_MENU_TIMING) is the measurement. Real clock: the daemons
// run their render loops on wall time, so the bound keeps the 50 ms budget and no more.
public sealed partial class MenuOpenLatencyTests(ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly TimeSpan OneWay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(50);
    private const int Opens = 3;

    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _running = [];
    private readonly FakePanes _homePanes = new();
    private readonly FakePanes _farPanes = new();
    private readonly ConcurrentQueue<string> _homeLog = new();
    private readonly ConcurrentQueue<string> _farLog = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private Endpoint _home = null!;
    private Endpoint _far = null!;

    private static Endpoint NewEndpoint()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        return new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
    }

    public Task InitializeAsync()
    {
        _home = NewEndpoint();
        _far = NewEndpoint();
        _running.Add(new FleetDaemon(new DaemonOptions
        {
            Endpoint = _far,
            Pty = _farPanes.NewPty,
            Terminal = _farPanes.NewTerminal,
            FleetExecutable = "fleet",
            WarmMenus = true,
            TimeMenus = true,
            Log = _farLog.Enqueue,
        }).RunAsync(_stop.Token));
        _running.Add(new FleetDaemon(new DaemonOptions
        {
            Endpoint = _home,
            Pty = _homePanes.NewPty,
            Terminal = SimulatedLinkTerminal.Over(_homePanes),
            FleetExecutable = "fleet",
            TimeMenus = true,
            Log = _homeLog.Enqueue,
            RemoteOpen = (_, _) =>
            {
                var link = new SimulatedLinkStream(_far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), OneWay);
                return new RemoteChannel(link, null, link);
            },
        }).RunAsync(_stop.Token));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await _stop.CancelAsync();
        await Task.WhenAll(_running);
    }

    private async Task<DaemonTests.TestClient> ClientAsync(Endpoint endpoint, string role, int cols = 0, int rows = 0, string? workspace = null)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(endpoint, role, cols, rows, workspace);
        _clients.Add(client);
        return client;
    }

    private FakePanes.FakePty[] FarMenus => [.. _farPanes.Started.Where(p => p.Program == "fleet")];

    private string[] HomeTimings => [.. _homeLog.Where(l => l.StartsWith("menu timing ", StringComparison.Ordinal))];

    [GeneratedRegex(@"^menu timing c\d+: (warm|cold), first frame (\d+) ms, settle (\d+) ms, link (\d+) ms via red@far$")]
    private static partial Regex LinkedLine();

    // The link's round trip as a client sees it: a control request answered by the far
    // fleetd through a second simulated link with the same one-way latency. Best of five.
    private async Task<double> MeasureRttAsync()
    {
        var link = new SimulatedLinkStream(await _far.ConnectAsync(TimeSpan.FromSeconds(5)), OneWay);
        using var wire = new Wire(link);
        await wire.SendAsync(
            MessageType.Hello,
            new Hello { Version = Wire.Version, Role = ClientRoles.Control, Os = "test" },
            WireJsonContext.Default.Hello);
        Assert.Equal(MessageType.Welcome, (await wire.ReceiveAsync())!.Value.Type);

        var best = double.MaxValue;
        for (var i = 1; i <= 5; i++)
        {
            var watch = Stopwatch.StartNew();
            await wire.SendAsync(MessageType.Request, new ControlRequest { Id = i, Op = "status" }, WireJsonContext.Default.ControlRequest);
            while (await wire.ReceiveAsync() is { } message && message.Type != MessageType.Response)
            {
            }

            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    private async Task<FakePanes.FakePty> ReadyWarmAsync(DaemonTests.TestClient far, int count)
    {
        await Eventually(() => Task.FromResult(FarMenus.Length == count));
        var warm = FarMenus[^1];
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "fit", Caller = warm.Env[FleetDaemon.PaneVariable], Cols = 40, Rows = 8 })).Ok);
        // A real menu redraws after the fit's resize; emitting before fleetd takes the
        // pre-fit baseline would keep the float hidden until RevealAfterFit.
        await Task.Delay(100);
        warm.Emit($"FAR-MENU-{count}\nitems\n");
        await Task.Delay(300);
        return warm;
    }

    [Fact]
    public async Task A_warm_menu_on_a_linked_machine_shows_its_first_frame_within_the_link_rtt_plus_50_ms()
    {
        var far = await ClientAsync(_far, ClientRoles.Control);
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);
        _farPanes.ByProgram("remote-claude")!.Emit("REMOTE-SCREEN");

        var home = await ClientAsync(_home, ClientRoles.Control);
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "local", Cwd = ".", Args = ["shell"] })).Ok);
        var window = await ClientAsync(_home, ClientRoles.Attach, 100, 30, "local");
        await window.WaitForFramesAsync(1);

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-connect", Host = "red@far" })).Ok);
        await Eventually(async () => (await home.RequestAsync(new ControlRequest { Op = "list-remotes" })).Remotes?.SingleOrDefault() is { State: RemoteLink.Connected });
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "show-remote", Client = window.Id, Host = "red@far", Workspace = "homelab" })).Ok);
        await window.WaitForAsync("REMOTE-SCREEN");

        var rtt = await MeasureRttAsync();
        output.WriteLine($"measured link rtt {rtt:0.0} ms (one way {OneWay.TotalMilliseconds} ms)");

        for (var open = 1; open <= Opens; open++)
        {
            var warm = await ReadyWarmAsync(far, open);

            await window.SendCommandAsync("menu");
            await window.WaitForAsync($"FAR-MENU-{open}");
            await Eventually(() => Task.FromResult(HomeTimings.Length == open));

            var line = HomeTimings[^1];
            output.WriteLine(line);
            var match = LinkedLine().Match(line);
            Assert.True(match.Success, line);
            Assert.Equal("warm", match.Groups[1].Value);
            var first = int.Parse(match.Groups[2].Value);
            Assert.True(first <= rtt + Budget.TotalMilliseconds, $"{line}; bound {rtt + Budget.TotalMilliseconds:0} ms");

            warm.Exit();
        }

        foreach (var farLine in _farLog.Where(l => l.StartsWith("menu timing ", StringComparison.Ordinal)))
        {
            output.WriteLine($"far {farLine}");
        }
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(await condition());
    }
}
