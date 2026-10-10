using System.Collections.Concurrent;
using Fleet.Platform.Forwards;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Tests.Platform.Forwards;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class RemoteForwardTests : IAsyncLifetime
{
    private const string Web = "LISTEN 0 511 127.0.0.1:5173 0.0.0.0:*\nLISTEN 0 511 127.0.0.1:9229 0.0.0.0:*\n";

    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _running = [];
    private readonly FakePanes _homePanes = new();
    private readonly FakePanes _farPanes = new();
    private readonly FakeSsh _ssh = new();
    private readonly ConcurrentQueue<string> _opened = new();
    private readonly ConcurrentQueue<Stream> _links = new();
    private int _nextLocal = 41000;
    private volatile bool _unreachable;
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
            ProjectConfigs = () =>
            [
                new ProjectConfigDto
                {
                    Name = "homelab",
                    Root = "/srv/homelab",
                    ForwardPorts = [5173],
                    RunCommand = "npm run dev",
                    ReadyPort = 5173,
                },
            ],
            OpenProject = async name =>
            {
                await using var opener = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Control, 0, 0, null);
                var opened = await opener.RequestAsync(new ControlRequest { Op = "spawn", Workspace = name, Cwd = ".", Args = ["opened-" + name] });
                return opened.Ok ? null : opened.Error;
            },
        });

        var home = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _home,
            Pty = _homePanes.NewPty,
            Terminal = _homePanes.NewTerminal,
            RemoteOpen = (_, _) =>
            {
                if (_unreachable)
                {
                    throw new IOException("ssh: connect to host nowhere port 22: Connection refused");
                }

                var stream = _far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                _links.Enqueue(stream);
                return new RemoteChannel(stream, null, stream);
            },
            Forwards = new ForwardOptions
            {
                ControlPath = host => "/tmp/cm-" + host,
                Ssh = _ssh.RunAsync,
                Locals = new LocalPorts(p => p < 41000 || p > _nextLocal - 1, () => Interlocked.Increment(ref _nextLocal)),
                OpenBrowser = url =>
                {
                    _opened.Enqueue(url);
                    return null;
                },
                Healthy = (_, _) => Task.FromResult(true),
                ScanEvery = TimeSpan.FromMilliseconds(50),
                ForwardWithin = TimeSpan.FromSeconds(5),
                StackWithin = TimeSpan.FromSeconds(5),
                ReconnectAfter = TimeSpan.FromMilliseconds(100),
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

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(await condition());
    }

    private async Task<IReadOnlyList<ForwardDto>> ForwardsAsync(Endpoint endpoint)
    {
        using var driver = new EmbeddedDriver(endpoint);
        return await driver.ForwardsAsync();
    }

    private async Task OpenFarProjectAsync()
    {
        await using var far = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Control, 0, 0, null);
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["claude"] })).Ok);
    }

    [Fact]
    public async Task An_allowlisted_port_is_forwarded_shown_to_the_remote_and_opened_on_the_viewer()
    {
        await OpenFarProjectAsync();
        _ssh.Listening = Web;
        using var home = new EmbeddedDriver(_home);

        await home.ConnectRemoteAsync("red@far");

        await Eventually(async () => (await ForwardsAsync(_home)).Any(f => f is { RemotePort: 5173, State: "forwarded", LocalPort: 5173 }));
        var rows = await ForwardsAsync(_home);
        Assert.Equal("detected", rows.Single(f => f.RemotePort == 9229).State);
        Assert.Equal("homelab", rows.Single(f => f.RemotePort == 5173).Project);
        Assert.Equal(["127.0.0.1:5173:127.0.0.1:5173"], _ssh.Forwards("forward"));

        await Eventually(async () => (await ForwardsAsync(_far)).Any(f => f is { Viewer: true, RemotePort: 5173, State: "forwarded" }));
        Assert.Equal(Environment.MachineName, (await ForwardsAsync(_far)).First(f => f.Viewer).Host);

        using var far = new EmbeddedDriver(_far);
        await far.ViewerOpenAsync(5173);
        await Eventually(() => Task.FromResult(_opened.Contains("http://localhost:5173")));
    }

    [Fact]
    public async Task A_manual_forward_is_made_on_request_and_removed_on_request()
    {
        _ssh.Listening = Web;
        using var home = new EmbeddedDriver(_home);

        var added = await home.AddForwardAsync("red@far", 9229, 19229);

        Assert.Equal(("forwarded", 19229), (added!.State, added.LocalPort));
        Assert.Contains("127.0.0.1:19229:127.0.0.1:9229", _ssh.Forwards("forward"));

        await home.RemoveForwardAsync("red@far", 9229);
        Assert.Contains("127.0.0.1:19229:127.0.0.1:9229", _ssh.Forwards("cancel"));
        Assert.Equal("detected", (await ForwardsAsync(_home)).Single(f => f.RemotePort == 9229).State);
    }

    [Fact]
    public async Task Starting_a_stack_runs_its_command_in_a_remote_pane_and_waits_for_its_port()
    {
        _ssh.Answer = args => args.Contains("-O") || _farPanes.ByProgram("sh") is null
            ? null
            : new Fleet.Platform.Forwards.Models.SshResult(0, Web, string.Empty);
        using var home = new EmbeddedDriver(_home);

        var up = await home.StartStackAsync("red@far", "homelab");

        Assert.Equal(("forwarded", 5173), (up!.State, up.LocalPort));
        var stack = _farPanes.ByProgram("sh");
        Assert.NotNull(stack);
        Assert.Equal(ForwardHub.StackCommand("npm run dev").Skip(1), stack!.Args);

        await home.StopStackAsync("red@far", "homelab");
        await Assert.ThrowsAsync<Fleet.Ports.Mux.Exceptions.MuxUnavailableException>(() => home.StopStackAsync("red@far", "homelab"));
    }

    [Fact]
    public async Task Two_stack_starts_at_once_run_the_command_once()
    {
        _ssh.Answer = args => args.Contains("-O") || _farPanes.ByProgram("sh") is null
            ? null
            : new Fleet.Platform.Forwards.Models.SshResult(0, Web, string.Empty);
        using var one = new EmbeddedDriver(_home);
        using var two = new EmbeddedDriver(_home);

        await Task.WhenAll(one.StartStackAsync("red@far", "homelab"), two.StartStackAsync("red@far", "homelab"));

        Assert.Single(_farPanes.Started, p => p.Program == "sh");
    }

    [Fact]
    public async Task Forwarding_from_a_host_that_cannot_be_reached_leaves_no_forward_behind()
    {
        _unreachable = true;
        using var home = new EmbeddedDriver(_home);

        await Assert.ThrowsAsync<Fleet.Ports.Mux.Exceptions.MuxUnavailableException>(() => home.AddForwardAsync("nowhere", 5173, null));

        Assert.Empty(await ForwardsAsync(_home));
    }

    [Fact]
    public async Task A_dropped_link_with_forwards_reconnects_and_restores_them()
    {
        await OpenFarProjectAsync();
        _ssh.Listening = Web;
        using var home = new EmbeddedDriver(_home);
        await home.ConnectRemoteAsync("red@far");
        await Eventually(async () => (await ForwardsAsync(_home)).Any(f => f is { RemotePort: 5173, State: "forwarded" }));

        Assert.True(_links.TryDequeue(out var first));
        await first.DisposeAsync();

        await Eventually(() => Task.FromResult(!_links.IsEmpty));
        await Eventually(async () => _ssh.Forwards("forward").Count() == 2
            && (await ForwardsAsync(_home)).Any(f => f is { RemotePort: 5173, State: "forwarded", LocalPort: 5173 }));
    }

    [Fact]
    public async Task Disconnecting_drops_every_forward()
    {
        await OpenFarProjectAsync();
        _ssh.Listening = Web;
        using var home = new EmbeddedDriver(_home);
        await home.ConnectRemoteAsync("red@far");
        await Eventually(async () => (await ForwardsAsync(_home)).Any(f => f.State == "forwarded"));

        await home.DisconnectRemoteAsync("red@far");

        Assert.Empty(await ForwardsAsync(_home));
    }
}
