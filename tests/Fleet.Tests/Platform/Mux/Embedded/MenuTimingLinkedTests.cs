using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Tests.Platform.Mux.Embedded;

// A home fleetd linked to a far fleetd through a SimulatedLinkStream, both timing menus.
// Later units reuse this shape to prove their gains on a slow link.
public sealed partial class MenuTimingLinkedTests : IAsyncLifetime
{
    private static readonly TimeSpan OneWay = TimeSpan.FromMilliseconds(100);

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

    private void Start(Endpoint endpoint, FakePanes panes, ConcurrentQueue<string> log, Func<string, string, RemoteChannel>? open = null)
    {
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = endpoint,
            Pty = panes.NewPty,
            Terminal = open is null ? panes.NewTerminal : SimulatedLinkTerminal.Over(panes),
            FleetExecutable = "fleet",
            RemoteOpen = open,
            TimeMenus = true,
            Log = log.Enqueue,
        });
        _running.Add(daemon.RunAsync(_stop.Token));
    }

    public Task InitializeAsync()
    {
        _home = NewEndpoint();
        _far = NewEndpoint();
        Start(_far, _farPanes, _farLog);
        Start(_home, _homePanes, _homeLog, (_, _) =>
        {
            var link = new SimulatedLinkStream(_far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), OneWay);
            return new RemoteChannel(link, null, link);
        });
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

    private static string[] Timings(ConcurrentQueue<string> log) =>
        [.. log.Where(l => l.StartsWith("menu timing ", StringComparison.Ordinal))];

    [GeneratedRegex(@"^menu timing c\d+: (warm|cold), first frame (\d+) ms, settle (\d+) ms, link (\d+) ms via red@far$")]
    private static partial Regex LinkedLine();

    [Fact]
    public async Task A_menu_opened_on_a_linked_workspace_logs_warm_or_cold_settle_and_link_time()
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

        await window.SendCommandAsync("menu");
        await Eventually(() => Task.FromResult(_farPanes.ByProgram("fleet") is not null));
        var menu = _farPanes.ByProgram("fleet")!;
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "fit", Caller = menu.Env[FleetDaemon.PaneVariable], Cols = 40, Rows = 8 })).Ok);
        await Task.Delay(100);
        menu.Emit("FAR-MENU\nitems\n");

        await window.WaitForAsync("FAR-MENU");
        await Eventually(() => Task.FromResult(Timings(_homeLog).Length == 1));

        var line = Assert.Single(Timings(_homeLog));
        var match = LinkedLine().Match(line);
        Assert.True(match.Success, line);
        Assert.Equal("cold", match.Groups[1].Value);
        var (first, settle, link) = (int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value));
        Assert.True(settle is >= 60 and < 1000, line);
        Assert.True(link >= 2 * OneWay.TotalMilliseconds - 10, line);
        Assert.True(first + 2 >= settle + link, line);
        Assert.Contains(Timings(_farLog), l => l.Contains(": cold, first frame", StringComparison.Ordinal) && !l.Contains("link", StringComparison.Ordinal));
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
