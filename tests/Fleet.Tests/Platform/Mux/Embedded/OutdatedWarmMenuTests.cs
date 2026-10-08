using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class OutdatedWarmMenuTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly string _executable = Path.Combine(Path.GetTempPath(), $"fleet-warm-{Guid.NewGuid():N}.exe");
    private Endpoint _endpoint = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        File.WriteAllText(_executable, "old build");
        File.SetLastWriteTimeUtc(_executable, DateTime.UtcNow.AddMinutes(-5));

        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = _executable,
            WarmMenus = true,
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
        File.Delete(_executable);
    }

    private IEnumerable<FakePanes.FakePty> Menus => _panes.Started.Where(p => p.Program == _executable);

    [Fact]
    public async Task A_warm_menu_started_before_fleet_was_updated_is_replaced_by_a_fresh_one()
    {
        var control = await DaemonTests.TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 100, 30, null);
        _clients.Add(control);
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = "techweb",
            NewWindow = true,
            Cwd = ".",
            Args = ["claude"],
        })).Ok);

        var client = await DaemonTests.TestClient.ConnectAsync(_endpoint, ClientRoles.Attach, 100, 30, "techweb");
        _clients.Add(client);
        await client.WaitForFramesAsync(1);
        await Eventually(() => Menus.Count() == 1);
        var warm = Menus.Single();

        File.WriteAllText(_executable, "new build");
        File.SetLastWriteTimeUtc(_executable, DateTime.UtcNow.AddMinutes(1));

        await client.SendCommandAsync("menu");

        await Eventually(() => warm.IsDisposed);
        await Eventually(() => Menus.Count(m => !m.IsDisposed) == 1);
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
