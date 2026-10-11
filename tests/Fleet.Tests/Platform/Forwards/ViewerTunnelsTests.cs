using Fleet.Platform.Forwards;
using Fleet.Platform.Forwards.Models;

namespace Fleet.Tests.Platform.Forwards;

// What `fleet attach --ssh` runs on a viewer without control sockets when the remote asks it to forward.
public sealed class ViewerTunnelsTests
{
    private readonly FakeTunnels _tunnels = new();
    private readonly List<string> _opened = [];
    private readonly LocalPorts _locals = new(p => p != 3000, () => 41000);

    private ViewerTunnels For() =>
        new(
            "red@box",
            new TunnelMode(_tunnels, _tunnels, TimeSpan.FromSeconds(2)) { RestartAfter = TimeSpan.FromMilliseconds(50) },
            _locals,
            url =>
            {
                lock (_opened)
                {
                    _opened.Add(url);
                }

                return null;
            },
            _ => { },
            TimeSpan.FromMilliseconds(20));

    [Fact]
    public async Task A_forward_runs_a_tunnel_to_the_attached_host_and_opens_its_local_port()
    {
        using var viewer = For();

        await viewer.ForwardAsync(5173, default);

        Assert.Equal(SshControl.Tunnel("red@box", 5173, "localhost", 5173), _tunnels.Started.Single().Arguments);
        Assert.Equal(["http://localhost:5173"], _opened);
    }

    [Fact]
    public async Task A_busy_local_port_opens_the_one_picked_instead()
    {
        using var viewer = For();

        await viewer.ForwardAsync(3000, default);

        Assert.Equal(["http://localhost:41000"], _opened);
    }

    [Fact]
    public async Task A_failed_tunnel_opens_nothing()
    {
        _tunnels.Exits = _ => (255, "red@box: Permission denied (publickey).");
        using var viewer = For();

        await viewer.ForwardAsync(5173, default);

        Assert.Empty(_opened);
    }

    [Fact]
    public async Task Unforwarding_kills_the_tunnel()
    {
        using var viewer = For();
        await viewer.ForwardAsync(5173, default);

        await viewer.UnforwardAsync(5173, default);

        Assert.True(_tunnels.Started.Single().Killed);
    }

    [Fact]
    public async Task A_tunnel_that_dies_is_started_again()
    {
        using var viewer = For();
        await viewer.ForwardAsync(5173, default);

        _tunnels.Started.Single().Exit(255, "Connection reset by peer");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_tunnels.Started.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, _tunnels.Started.Count);
    }

    [Fact]
    public async Task Applying_a_forward_effect_forwards_and_an_unforward_effect_stops_it()
    {
        using var viewer = For();

        viewer.Apply(forward: true, 5173);
        await WaitUntil(() => _opened.Contains("http://localhost:5173"));

        viewer.Apply(forward: false, 5173);
        await WaitUntil(() => _tunnels.Started.Single().Killed);
    }

    [Fact]
    public async Task Disposing_kills_every_tunnel_and_the_launcher()
    {
        var viewer = For();
        await viewer.ForwardAsync(5173, default);
        await viewer.ForwardAsync(9229, default);

        viewer.Dispose();

        Assert.All(_tunnels.Started, t => Assert.True(t.Killed));
        Assert.True(_tunnels.Disposed);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
