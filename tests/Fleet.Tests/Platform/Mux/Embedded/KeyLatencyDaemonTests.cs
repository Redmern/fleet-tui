using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed partial class KeyLatencyDaemonTests : IAsyncLifetime
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

    private async Task<(DaemonTests.TestClient Client, FakePanes.FakePty Pane)> StartAsync(bool timeKeys)
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = "fleet",
            WarmMenus = false,
            TimeKeys = timeKeys,
            Log = _log.Enqueue,
        });
        _running.Add(daemon.RunAsync(_stop.Token));

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
        return (client, _panes.ByProgram("claude")!);
    }

    private string[] Timings => [.. _log.Where(l => l.StartsWith("key timing ", StringComparison.Ordinal))];

    [GeneratedRegex(@"^key timing c\d+ \S+: output ([\d.]+) ms, frame ([\d.]+) ms$")]
    private static partial Regex KeyLine();

    [Fact]
    public async Task A_key_the_pane_answers_logs_its_output_and_frame_time()
    {
        var (client, pane) = await StartAsync(timeKeys: true);

        await client.SendKeyAsync("j");
        await Eventually(() => pane.Written == "j");
        pane.Emit("MOVED");
        await client.WaitForAsync("MOVED");
        await Eventually(() => Timings.Length == 1);

        var match = KeyLine().Match(Assert.Single(Timings));
        Assert.True(match.Success, Timings[0]);
        Assert.True(double.Parse(match.Groups[2].Value) >= double.Parse(match.Groups[1].Value));
    }

    [Fact]
    public async Task Without_the_switch_keys_log_no_timing()
    {
        var (client, pane) = await StartAsync(timeKeys: false);

        await client.SendKeyAsync("j");
        await Eventually(() => pane.Written == "j");
        pane.Emit("MOVED");
        await client.WaitForAsync("MOVED");
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
