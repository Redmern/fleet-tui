using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed partial class MenuTimingDaemonTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _log = new();
    private readonly List<Task> _running = [];
    private Endpoint _endpoint = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await _stop.CancelAsync();
        await Task.WhenAll(_running);
    }

    private void Start(bool timeMenus, bool warmMenus)
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = "fleet",
            WarmMenus = warmMenus,
            TimeMenus = timeMenus,
            Log = _log.Enqueue,
        });
        _running.Add(daemon.RunAsync(_stop.Token));
    }

    private async Task<DaemonTests.TestClient> ConnectAsync(string role, string? workspace = null)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(_endpoint, role, 100, 30, workspace);
        _clients.Add(client);
        return client;
    }

    private async Task<(DaemonTests.TestClient Control, DaemonTests.TestClient Client)> ProjectAsync()
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
        return (control, client);
    }

    private static async Task DrawMenuAsync(DaemonTests.TestClient control, FakePanes.FakePty menu)
    {
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "fit",
            Caller = menu.Env[FleetDaemon.PaneVariable],
            Cols = 40,
            Rows = 8,
        })).Ok);
        menu.Emit("THE-MENU\nitems\n");
    }

    private string[] Timings => [.. _log.Where(l => l.StartsWith("menu timing ", StringComparison.Ordinal))];

    [GeneratedRegex(@"^menu timing c\d+: (warm|cold), first frame (\d+) ms, settle (\d+) ms$")]
    private static partial Regex LocalLine();

    [Fact]
    public async Task A_cold_menu_open_logs_its_first_frame_and_settle_time()
    {
        Start(timeMenus: true, warmMenus: false);
        var (control, client) = await ProjectAsync();

        await client.SendCommandAsync("menu");
        await Eventually(() => _panes.ByProgram("fleet") is not null);
        await DrawMenuAsync(control, _panes.ByProgram("fleet")!);
        await client.WaitForAsync("THE-MENU");
        await Eventually(() => Timings.Length == 1);

        var match = LocalLine().Match(Assert.Single(Timings));
        Assert.True(match.Success, Timings[0]);
        Assert.Equal("cold", match.Groups[1].Value);
        Assert.True(int.Parse(match.Groups[2].Value) >= int.Parse(match.Groups[3].Value));
        Assert.True(int.Parse(match.Groups[3].Value) >= 60, "a cold menu waits at least the quiet time");
    }

    [Fact]
    public async Task A_warm_menu_open_logs_warm()
    {
        Start(timeMenus: true, warmMenus: true);
        var (control, client) = await ProjectAsync();
        await Eventually(() => _panes.ByProgram("fleet") is not null);
        await DrawMenuAsync(control, _panes.ByProgram("fleet")!);
        await Task.Delay(300);

        await client.SendCommandAsync("menu");
        await client.WaitForAsync("THE-MENU");
        await Eventually(() => Timings.Length == 1);

        var match = LocalLine().Match(Assert.Single(Timings));
        Assert.True(match.Success, Timings[0]);
        Assert.Equal("warm", match.Groups[1].Value);
    }

    [Fact]
    public async Task Without_the_switch_a_menu_open_logs_no_timing()
    {
        Start(timeMenus: false, warmMenus: false);
        var (control, client) = await ProjectAsync();

        await client.SendCommandAsync("menu");
        await Eventually(() => _panes.ByProgram("fleet") is not null);
        await DrawMenuAsync(control, _panes.ByProgram("fleet")!);
        await client.WaitForAsync("THE-MENU");
        await Task.Delay(100);

        Assert.Empty(Timings);
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
