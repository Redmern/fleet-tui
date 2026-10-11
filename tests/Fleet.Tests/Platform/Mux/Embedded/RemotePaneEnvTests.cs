using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class RemotePaneEnvTests
{
    [Fact]
    public async Task A_daemon_serving_remote_viewers_runs_its_hook_on_every_pane_env()
    {
        var pane = await SpawnAsync(env => env["PATH"] = "/shims:" + (env.TryGetValue("PATH", out var path) ? path : "/bin"));

        Assert.Equal("/shims:/bin", pane.Env["PATH"]);
        Assert.False(string.IsNullOrEmpty(pane.Env[FleetDaemon.PaneVariable]));
    }

    [Fact]
    public async Task A_local_daemon_leaves_the_pane_env_alone()
    {
        var pane = await SpawnAsync(null);

        Assert.False(pane.Env.ContainsKey("PATH"));
    }

    private static async Task<FakePanes.FakePty> SpawnAsync(Action<IDictionary<string, string>>? remotePaneEnv)
    {
        var panes = new FakePanes();
        using var stop = new CancellationTokenSource();
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        var endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var running = new FleetDaemon(new DaemonOptions
        {
            Endpoint = endpoint,
            Pty = panes.NewPty,
            Terminal = panes.NewTerminal,
            RemotePaneEnv = remotePaneEnv,
        }).RunAsync(stop.Token);

        await using (var control = await DaemonTests.TestClient.ConnectAsync(endpoint, ClientRoles.Control, 0, 0, null))
        {
            var spawned = await control.RequestAsync(new ControlRequest
            {
                Op = "spawn",
                Session = "rib",
                NewWindow = true,
                Cwd = ".",
                Args = ["claude"],
            });
            Assert.True(spawned.Ok, spawned.Error);
        }

        await stop.CancelAsync();
        await running;
        return panes.ByProgram("claude")!;
    }
}
