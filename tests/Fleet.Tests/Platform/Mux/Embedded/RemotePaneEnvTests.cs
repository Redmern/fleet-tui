using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class RemotePaneEnvTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private Endpoint _endpoint = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
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

    private static void Shims(IDictionary<string, string> env) =>
        env["PATH"] = "/shims:" + (env.TryGetValue("PATH", out var path) ? path : "/bin");

    private void Start(bool servesBridge, Action<IDictionary<string, string>>? remotePaneEnv) =>
        _running = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            ServesBridge = servesBridge,
            RemotePaneEnv = remotePaneEnv,
        }).RunAsync(_stop.Token);

    [Fact]
    public async Task A_daemon_started_for_a_bridge_runs_its_remote_hook_on_every_pane_env()
    {
        Start(servesBridge: true, Shims);

        var pane = await SpawnAsync("claude");

        Assert.Equal("/shims:/bin", pane.Env["PATH"]);
        Assert.False(string.IsNullOrEmpty(pane.Env[FleetDaemon.PaneVariable]));
        Assert.False(string.IsNullOrEmpty(pane.Env[FleetDaemon.PaneTokenVariable]));
    }

    [Fact]
    public async Task A_daemon_with_only_local_viewers_leaves_the_pane_env_alone_even_with_a_remote_hook()
    {
        Start(servesBridge: false, Shims);
        await ConnectAsync(ClientRoles.Attach, bridged: false);

        var pane = await SpawnAsync("claude");

        Assert.False(pane.Env.ContainsKey("PATH"));
        Assert.False(pane.Env.ContainsKey(FleetDaemon.PaneTokenVariable));
    }

    [Fact]
    public async Task Once_a_remote_viewer_attaches_new_panes_get_the_remote_hook()
    {
        Start(servesBridge: false, Shims);
        var before = await SpawnAsync("before");
        await ConnectAsync(ClientRoles.Attach, bridged: true);

        var after = await SpawnAsync("after");

        Assert.False(before.Env.ContainsKey("PATH"));
        Assert.Equal("/shims:/bin", after.Env["PATH"]);
    }

    [Fact]
    public async Task A_daemon_without_a_hook_leaves_the_pane_env_alone()
    {
        Start(servesBridge: true, null);

        var pane = await SpawnAsync("claude");

        Assert.False(pane.Env.ContainsKey("PATH"));
    }

    private async Task<DaemonTests.TestClient> ConnectAsync(string role, bool bridged)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(_endpoint, role, 60, 12, null, bridged);
        _clients.Add(client);
        return client;
    }

    private async Task<FakePanes.FakePty> SpawnAsync(string program)
    {
        var control = await ConnectAsync(ClientRoles.Control, bridged: false);
        var spawned = await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = "rib-" + program,
            NewWindow = true,
            Cwd = ".",
            Args = [program],
        });
        Assert.True(spawned.Ok, spawned.Error);
        return _panes.ByProgram(program)!;
    }
}
