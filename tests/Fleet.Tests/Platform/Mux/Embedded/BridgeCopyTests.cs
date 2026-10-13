using System.Collections.Concurrent;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class BridgeCopyTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _running = [];
    private readonly FakePanes _homePanes = new();
    private readonly FakePanes _farPanes = new();
    private readonly ConcurrentQueue<string> _farLog = new();
    private readonly ConcurrentQueue<string> _homeLog = new();
    private readonly List<IAsyncDisposable> _clients = [];
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
            ServesBridge = true,
            Log = _farLog.Enqueue,
        }).RunAsync(_stop.Token));
        _running.Add(new FleetDaemon(new DaemonOptions
        {
            Endpoint = _home,
            Pty = _homePanes.NewPty,
            Terminal = _homePanes.NewTerminal,
            Log = _homeLog.Enqueue,
            RemoteOpen = (_, _) =>
            {
                var stream = _far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                return new RemoteChannel(stream, null, stream);
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

    [Fact]
    public async Task A_copy_in_a_remote_pane_reaches_the_clipboard_of_the_local_viewer_showing_it()
    {
        var (farPane, viewer) = await ShowRemotePaneAsync();

        FarTerminal(farPane).Copy("remote yank");

        await Eventually(() => viewer.Effects.Any(e => e.Kind == HostEffects.Clipboard && e.Value == "remote yank"));
    }

    [Fact]
    public async Task Copy_mode_yank_in_a_remote_pane_reaches_the_clipboard_of_the_local_viewer()
    {
        var (farPane, viewer) = await ShowRemotePaneAsync();
        var far = _farPanes.Started.Single(p => p.Env.GetValueOrDefault(FleetDaemon.PaneVariable) == farPane);
        far.Emit(string.Join('\n', Enumerable.Range(1, 40).Select(i => $"line {i}")));

        await viewer.SendCommandAsync("copy-mode");
        foreach (var step in Enumerable.Repeat("up", 3))
        {
            await viewer.SendCommandAsync("copy", step);
        }

        await viewer.SendCommandAsync("copy", "yank");

        await Eventually(() => viewer.Effects.Any(e => e.Kind == HostEffects.Clipboard && e.Value?.StartsWith("line", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task A_copy_in_a_remote_pane_reaches_a_direct_bridged_viewer_when_the_local_link_shows_something_else()
    {
        var (farPane, viewer) = await ShowRemotePaneAsync();
        var direct = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Attach, 60, 12, "homelab", bridged: true);
        _clients.Add(direct);
        await Eventually(() => direct.FrameCount > 0);
        await viewer.SendCommandAsync("show", "local");
        await Task.Delay(300);
        await direct.SendKeyAsync("x");
        await Task.Delay(100);

        FarTerminal(farPane).Copy("remote yank");

        await Eventually(() => direct.Effects.Any(e => e.Kind == HostEffects.Clipboard && e.Value == "remote yank"));
    }

    [Fact]
    public async Task A_copy_with_no_viewer_to_take_it_is_logged_with_the_reason()
    {
        var far = await ControlAsync(_far);
        var spawned = await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] });
        Assert.True(spawned.Ok, spawned.Error);

        FarTerminal(spawned.Pane!).Copy("nobody sees this");

        await Eventually(() => _farLog.Any(l => l.Contains($"{spawned.Pane} copy of 16 chars dropped: no client is attached", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_remote_copy_no_local_window_shows_is_logged_with_the_reason()
    {
        var (farPane, viewer) = await ShowRemotePaneAsync();
        await viewer.SendCommandAsync("show", "local");
        await Task.Delay(300);

        FarTerminal(farPane).Copy("remote yank");

        await Eventually(() => _homeLog.Any(l => l.Contains("copy of 11 chars dropped: no window shows @", StringComparison.Ordinal)));
        Assert.DoesNotContain(viewer.Effects, e => e.Kind == HostEffects.Clipboard);
    }

    private FakePanes.FakeTerminal FarTerminal(string pane) =>
        _farPanes.Started.Single(p => p.Env.GetValueOrDefault(FleetDaemon.PaneVariable) == pane).Terminal;

    private async Task<(string FarPane, DaemonTests.TestClient Viewer)> ShowRemotePaneAsync()
    {
        var far = await ControlAsync(_far);
        var spawned = await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] });
        Assert.True(spawned.Ok, spawned.Error);

        var home = await ControlAsync(_home);
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "local", Cwd = ".", Args = ["shell"] })).Ok);
        var viewer = await DaemonTests.TestClient.ConnectAsync(_home, ClientRoles.Attach, 60, 12, "local");
        _clients.Add(viewer);
        await Eventually(() => viewer.FrameCount > 0);

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-connect", Host = "red@far" })).Ok);
        await EventuallyAsync(async () => (await home.RequestAsync(new ControlRequest { Op = "list-remotes" })).Remotes?.SingleOrDefault() is { State: RemoteLink.Connected });
        var shown = await home.RequestAsync(new ControlRequest { Op = "show-remote", Client = viewer.Id, Host = "red@far", Workspace = "homelab" });
        Assert.True(shown.Ok, shown.Error);
        await EventuallyAsync(async () => (await far.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes?.Any(p => p.Id == spawned.Pane && p.Active) == true);
        await Task.Delay(200);
        return (spawned.Pane!, viewer);
    }

    private async Task<DaemonTests.TestClient> ControlAsync(Endpoint endpoint)
    {
        var control = await DaemonTests.TestClient.ConnectAsync(endpoint, ClientRoles.Control, 0, 0, null);
        _clients.Add(control);
        return control;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }
}
