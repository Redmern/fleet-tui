using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared.Constants;

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

    [Fact]
    public async Task Only_panes_in_a_workspace_a_remote_viewer_has_open_are_marked_remote()
    {
        Start(servesBridge: false, Shims);
        var first = await SpawnAsync("first", "rib-remote");
        await ConnectAsync(ClientRoles.Attach, bridged: true, workspace: "rib-remote");

        var local = await SpawnAsync("local", "rib-local");
        var remote = await SpawnAsync("remote", "rib-remote");

        Assert.False(first.Env.ContainsKey(AgentHarness.RemotePaneVariable));
        Assert.False(local.Env.ContainsKey(AgentHarness.RemotePaneVariable));
        Assert.Equal("/shims:/bin", local.Env["PATH"]);
        Assert.Equal("1", remote.Env[AgentHarness.RemotePaneVariable]);
    }

    [Fact]
    public async Task A_daemon_started_for_a_bridge_does_not_mark_a_pane_no_remote_viewer_has_open()
    {
        Start(servesBridge: true, Shims);
        await ConnectAsync(ClientRoles.Attach, bridged: false, workspace: null);

        var pane = await SpawnAsync("claude");

        Assert.False(pane.Env.ContainsKey(AgentHarness.RemotePaneVariable));
    }

    [Fact]
    public async Task A_pane_env_asking_for_the_remote_marker_keeps_it_only_on_a_daemon_serving_remote_viewers()
    {
        Start(servesBridge: false, Shims);

        var pane = await SpawnAsync("claude", env: new Dictionary<string, string> { [AgentHarness.RemotePaneVariable] = "1" });

        Assert.False(pane.Env.ContainsKey(AgentHarness.RemotePaneVariable));
    }

    private async Task<DaemonTests.TestClient> ConnectAsync(string role, bool bridged, string? workspace = null)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(_endpoint, role, 60, 12, workspace, bridged);
        _clients.Add(client);
        return client;
    }

    private async Task<FakePanes.FakePty> SpawnAsync(
        string program, string? session = null, Dictionary<string, string>? env = null)
    {
        var control = await ConnectAsync(ClientRoles.Control, bridged: false);
        var spawned = await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = session ?? "rib-" + program,
            Env = env,
            NewWindow = true,
            Cwd = ".",
            Args = [program],
        });
        Assert.True(spawned.Ok, spawned.Error);
        return _panes.ByProgram(program)!;
    }
}
