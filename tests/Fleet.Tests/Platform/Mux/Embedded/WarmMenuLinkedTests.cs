using System.Collections.Concurrent;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Tests.Platform.Mux.Embedded;

// The MenuTimingLinkedTests shape with warm menus on the far fleetd: a menu opened with an
// action over the slow link reuses the far warm menu instead of starting a cold one.
public sealed class WarmMenuLinkedTests : IAsyncLifetime
{
    private static readonly TimeSpan OneWay = TimeSpan.FromMilliseconds(100);

    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _running = [];
    private readonly FakePanes _homePanes = new();
    private readonly FakePanes _farPanes = new();
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

    private int FarMenus => _farPanes.Started.Count(p => p.Program == "fleet");

    [Fact]
    public async Task A_menu_opened_with_an_action_on_a_linked_workspace_reuses_the_far_warm_menu()
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

        await Eventually(() => Task.FromResult(FarMenus == 1));
        var warm = _farPanes.ByProgram("fleet")!;
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "fit", Caller = warm.Env[FleetDaemon.PaneVariable], Cols = 40, Rows = 8 })).Ok);
        warm.Emit("FAR-MENU\nitems\n");
        var waiting = far.RequestAsync(new ControlRequest { Op = FleetDaemon.MenuWaitOp, Caller = warm.Env[FleetDaemon.PaneVariable] });
        await Task.Delay(100);

        await window.SendCommandAsync("menu", "switch-project");

        var opened = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("switch-project", opened.Text);
        await window.WaitForAsync("FAR-MENU");
        Assert.Equal(1, FarMenus);
        await Eventually(() => Task.FromResult(_farLog.Any(l => l.StartsWith("menu timing ", StringComparison.Ordinal))));
        Assert.Contains(_farLog, l => l.StartsWith("menu timing ", StringComparison.Ordinal) && l.Contains(": warm,", StringComparison.Ordinal));
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
