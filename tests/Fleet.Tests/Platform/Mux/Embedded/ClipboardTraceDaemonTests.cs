using System.Collections.Concurrent;
using System.Text.Json;
using Fleet.Platform.Clipboard;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using static Fleet.Tests.Platform.Mux.Embedded.DaemonTests;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class ClipboardTraceDaemonTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly ConcurrentQueue<string> _log = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly string _trace = Path.Combine(Path.GetTempPath(), $"fleet-trace-{Guid.NewGuid():N}.ndjson");
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
            ServesBridge = true,
            Log = _log.Enqueue,
            ClipboardTrace = new ClipboardTrace(_trace),
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
        File.Delete(_trace);
    }

    [Fact]
    public async Task An_osc_52_copy_the_terminal_drops_is_logged_and_traced_with_its_reason()
    {
        var pane = await SpawnAsync("techweb", "nvim");

        _panes.ByProgram("nvim")!.Terminal.DropCopy("no text/plain content, only image/png");

        Assert.Contains(_log, l => l == $"{pane} osc 52 copy dropped: no text/plain content, only image/png");
        var hop = Assert.Single(await HopsAsync());
        Assert.Equal("pane-osc52-dropped", hop.GetProperty("hop").GetString());
        Assert.Equal(pane, hop.GetProperty("pane").GetString());
        Assert.Equal("no text/plain content, only image/png", hop.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_copy_is_traced_at_every_daemon_hop_with_the_same_sha256()
    {
        var pane = await SpawnAsync("techweb", "nvim");
        var viewer = await ViewAsync("techweb");

        _panes.ByProgram("nvim")!.Terminal.Copy("yanked line\n");

        await ClipboardImageDaemonTests.Eventually(() => viewer.Effects.Any(e => e.Kind == HostEffects.Clipboard));
        var hops = await HopsAsync();
        Assert.Equal(["pane-osc52", "copy-target"], hops.Select(h => h.GetProperty("hop").GetString()));
        Assert.All(hops, h => Assert.Equal(ClipboardTrace.Sha256("yanked line\n"), h.GetProperty("sha256").GetString()));
        Assert.Equal(viewer.Id, hops[1].GetProperty("detail").GetString());
        Assert.Equal(pane, hops[1].GetProperty("pane").GetString());
    }

    [Fact]
    public async Task A_copy_with_no_viewer_to_take_it_is_traced_as_dropped()
    {
        await SpawnAsync("techweb", "nvim");

        _panes.ByProgram("nvim")!.Terminal.Copy("nobody sees this");

        await ClipboardImageDaemonTests.Eventually(() => File.Exists(_trace) && File.ReadAllText(_trace).Contains("copy-dropped", StringComparison.Ordinal));
        Assert.Equal(["pane-osc52", "copy-dropped"], (await HopsAsync()).Select(h => h.GetProperty("hop").GetString()));
    }

    private async Task<List<JsonElement>> HopsAsync() =>
        [.. (await File.ReadAllLinesAsync(_trace)).Select(line => JsonDocument.Parse(line).RootElement)];

    private async Task<TestClient> ControlAsync()
    {
        var control = await TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null);
        _clients.Add(control);
        return control;
    }

    private async Task<TestClient> ViewAsync(string workspace)
    {
        var viewer = await TestClient.ConnectAsync(_endpoint, ClientRoles.Attach, 60, 12, workspace, bridged: true);
        _clients.Add(viewer);
        await ClipboardImageDaemonTests.Eventually(() => viewer.FrameCount > 0);
        return viewer;
    }

    private async Task<string> SpawnAsync(string workspace, string program)
    {
        var spawned = await (await ControlAsync()).RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = workspace,
            NewWindow = true,
            Cwd = ".",
            Args = [program],
        });

        Assert.True(spawned.Ok, spawned.Error);
        return spawned.Pane!;
    }
}
