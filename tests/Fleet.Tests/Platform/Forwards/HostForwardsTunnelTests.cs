using Fleet.Platform.Forwards;
using Fleet.Platform.Forwards.Models;
using Fleet.Ports.Forwards.Enums;

namespace Fleet.Tests.Platform.Forwards;

// Without OpenSSH control sockets (Windows) each forward is its own background `ssh -N -L`.
public sealed class HostForwardsTunnelTests
{
    private readonly FakeSsh _ssh = new();
    private readonly FakeTunnels _tunnels = new();
    private readonly LocalPorts _locals = new(_ => true, () => 41000);

    private HostForwards For(LocalPorts? locals = null) =>
        new(
            "red@box",
            null,
            locals ?? _locals,
            _ssh.RunAsync,
            _ => { },
            new TunnelMode(_tunnels, _tunnels, TimeSpan.FromSeconds(2)));

    private static async Task ScanAndReconcileAsync(HostForwards forwards)
    {
        var listening = await forwards.ScanAsync(default);
        await forwards.ReconcileAsync(listening!, new Dictionary<int, string>(), default);
    }

    [Fact]
    public async Task A_pinned_port_is_forwarded_by_a_background_tunnel_once_its_local_port_accepts()
    {
        var forwards = For();
        forwards.Pin(5173, null);

        await ScanAndReconcileAsync(forwards);

        Assert.Empty(_ssh.Calls);
        var tunnel = Assert.Single(_tunnels.Started);
        Assert.Equal(SshControl.Tunnel("red@box", 5173, "localhost", 5173), tunnel.Arguments);
        var row = forwards.Row(5173);
        Assert.Equal(ForwardState.Forwarded, row.State);
        Assert.Equal("http://localhost:5173", row.Url);
    }

    [Fact]
    public async Task A_busy_local_port_is_swapped_for_a_free_one()
    {
        var forwards = For(new LocalPorts(p => p != 5173, () => 41000));
        forwards.Pin(5173, null);

        await ScanAndReconcileAsync(forwards);

        Assert.Equal(["127.0.0.1:41000:localhost:5173"], _tunnels.Specs);
        Assert.Equal(41000, forwards.Row(5173).LocalPort);
    }

    [Fact]
    public async Task A_tunnel_that_exits_early_fails_the_row_with_what_ssh_said()
    {
        _tunnels.Exits = _ => (255, "red@box: Permission denied (publickey).\r\n");
        var forwards = For();
        forwards.Pin(5173, null);

        await ScanAndReconcileAsync(forwards);

        var row = forwards.Row(5173);
        Assert.Equal(ForwardState.Failed, row.State);
        Assert.Equal("red@box: Permission denied (publickey).", row.Error);
        Assert.False(_locals.Holds(5173));
    }

    [Fact]
    public async Task A_tunnel_that_never_opens_its_port_is_killed_and_failed()
    {
        var forwards = new HostForwards(
            "red@box",
            null,
            _locals,
            _ssh.RunAsync,
            _ => { },
            new TunnelMode(_tunnels, new Never(), TimeSpan.FromMilliseconds(300)));
        forwards.Pin(5173, null);

        await ScanAndReconcileAsync(forwards);

        Assert.True(_tunnels.Started.Single().Killed);
        Assert.Equal(ForwardState.Failed, forwards.Row(5173).State);
        Assert.False(_locals.Holds(5173));
    }

    [Fact]
    public async Task Unforwarding_kills_the_tunnel_and_removes_the_row()
    {
        var forwards = For();
        await forwards.ReconcileAsync(new Dictionary<int, IReadOnlyList<string>>(), new Dictionary<int, string>(), default);
        await forwards.ForwardNowAsync(5173, null, default);
        Assert.Equal(ForwardState.Forwarded, forwards.Row(5173).State);

        await forwards.UnforwardNowAsync(5173, default);

        Assert.True(_tunnels.Started.Single().Killed);
        Assert.DoesNotContain(forwards.Rows(), r => r.RemotePort == 5173);
        Assert.False(_locals.Holds(5173));
    }

    [Fact]
    public async Task A_dropped_link_kills_its_tunnels()
    {
        var forwards = For();
        forwards.Pin(5173, null);
        await ScanAndReconcileAsync(forwards);

        forwards.Unlinked();

        Assert.True(_tunnels.Started.Single().Killed);
        Assert.Equal(ForwardState.Waiting, forwards.Row(5173).State);
    }

    private sealed class Never : Fleet.Ports.Forwards.IListenerProbe
    {
        public Task<bool> ListeningAsync(int port, CancellationToken ct = default) => Task.FromResult(false);
    }
}
