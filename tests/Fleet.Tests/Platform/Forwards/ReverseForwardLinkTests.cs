using System.Collections.Concurrent;
using Fleet.Platform.Forwards;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Tests.Platform.Mux.Embedded;

namespace Fleet.Tests.Platform.Forwards;

public sealed class ReverseForwardLinkTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _running = [];
    private readonly FakePanes _homePanes = new();
    private readonly FakePanes _farPanes = new();
    private readonly FakeSsh _ssh = new() { Listening = "LISTEN 0 511 127.0.0.1:5173 0.0.0.0:*\n" };
    private readonly ConcurrentQueue<Stream> _links = new();
    private readonly ConcurrentQueue<(string Host, string? ControlPath)> _ups = new();
    private int _nextLocal = 43000;
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

        var far = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _far,
            Pty = _farPanes.NewPty,
            Terminal = _farPanes.NewTerminal,
            SavedProjects = () => ["homelab"],
            ProjectConfigs = () => [new ProjectConfigDto { Name = "homelab", Root = "/srv/homelab", ForwardPorts = [5173] }],
        });

        var home = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _home,
            Pty = _homePanes.NewPty,
            Terminal = _homePanes.NewTerminal,
            RemoteOpen = (_, _) =>
            {
                var stream = _far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                _links.Enqueue(stream);
                return new RemoteChannel(stream, null, stream);
            },
            Forwards = new ForwardOptions
            {
                ControlPath = host => "/tmp/cm-" + host,
                Ssh = _ssh.RunAsync,
                Locals = new LocalPorts(p => p < 43000 || p > _nextLocal - 1, () => Interlocked.Increment(ref _nextLocal)),
                Healthy = (_, _) => Task.FromResult(true),
                ScanEvery = TimeSpan.FromMilliseconds(50),
                ReconnectAfter = TimeSpan.FromMilliseconds(100),
                LinkUp = (host, controlPath, _) =>
                {
                    _ups.Enqueue((host, controlPath));
                    return Task.CompletedTask;
                },
            },
        });

        _running.Add(far.RunAsync(_stop.Token));
        _running.Add(home.RunAsync(_stop.Token));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        await Task.WhenAll(_running);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(50);
        }

        Assert.True(condition());
    }

    private async Task OpenFarProjectAsync()
    {
        await using var far = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Control, 0, 0, null);
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["claude"] })).Ok);
    }

    [Fact]
    public async Task A_link_coming_up_runs_the_link_up_hook_once_with_its_control_socket()
    {
        using var home = new EmbeddedDriver(_home);

        await home.ConnectRemoteAsync("red@far");

        await Eventually(() => !_ups.IsEmpty);
        await Task.Delay(300);
        Assert.Equal([("red@far", "/tmp/cm-red@far")], _ups.ToArray());
    }

    [Fact]
    public async Task A_link_that_reconnects_runs_the_link_up_hook_again()
    {
        await OpenFarProjectAsync();
        using var home = new EmbeddedDriver(_home);
        await home.ConnectRemoteAsync("red@far");
        await Eventually(() => _ups.Count == 1 && _ssh.Forwards("forward").Any());

        Assert.True(_links.TryDequeue(out var first));
        await first.DisposeAsync();

        await Eventually(() => _ups.Count == 2);
    }
}
