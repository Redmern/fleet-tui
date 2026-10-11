using Fleet.Platform.Forwards;
using Fleet.Platform.Forwards.Models;
using Fleet.Ports.Forwards.Enums;
using Fleet.Tests.Platform.Mux.Embedded;

namespace Fleet.Tests.Platform.Forwards;

// Without OpenSSH control sockets (Windows) each forward is its own background `ssh -N -L`.
public sealed class HostForwardsTunnelTests
{
    private readonly FakeSsh _ssh = new();
    private readonly FakeTunnels _tunnels = new();
    private readonly LocalPorts _locals = new(_ => true, () => 41000);
    private readonly SimulatedLinkClock _clock = new();

    private HostForwards For(LocalPorts? locals = null) =>
        new(
            "red@box",
            null,
            locals ?? _locals,
            _ssh.RunAsync,
            _ => { },
            new TunnelMode(_tunnels, _tunnels, TimeSpan.FromSeconds(2))
            {
                RestartAfter = TimeSpan.FromSeconds(5),
                RestartAtMost = TimeSpan.FromSeconds(60),
                Time = _clock,
            });

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

    [Fact]
    public async Task A_tunnel_that_dies_while_pinned_waits_out_the_backoff_then_comes_back_on_its_port()
    {
        var forwards = For(new LocalPorts(p => p != 5173, () => 41000));
        forwards.Pin(5173, null);
        await ScanAndReconcileAsync(forwards);

        _tunnels.Started.Single().Exit(255, "Connection reset by peer\r\n");

        var row = forwards.Row(5173);
        Assert.Equal(ForwardState.Waiting, row.State);
        Assert.Contains("Connection reset by peer", row.Error);

        await ScanAndReconcileAsync(forwards);
        Assert.Single(_tunnels.Started);
        Assert.Contains("restarting in 5s", forwards.Row(5173).Error);

        _clock.Advance(TimeSpan.FromSeconds(5));
        await ScanAndReconcileAsync(forwards);

        Assert.Equal(2, _tunnels.Started.Count);
        Assert.Equal(["127.0.0.1:41000:localhost:5173", "127.0.0.1:41000:localhost:5173"], _tunnels.Specs);
        Assert.Equal(ForwardState.Forwarded, forwards.Row(5173).State);
    }

    [Fact]
    public async Task A_tunnel_that_keeps_dying_backs_off_twice_as_long_each_time_up_to_the_cap()
    {
        var forwards = For();
        forwards.Pin(5173, null);
        await ScanAndReconcileAsync(forwards);

        foreach (var wait in new[] { 5, 10, 20, 40, 60, 60 })
        {
            _tunnels.Started.Last().Exit(255, "gone");
            await ScanAndReconcileAsync(forwards);
            var started = _tunnels.Started.Count;

            _clock.Advance(TimeSpan.FromSeconds(wait) - TimeSpan.FromMilliseconds(1));
            await ScanAndReconcileAsync(forwards);
            Assert.Equal(started, _tunnels.Started.Count);

            _clock.Advance(TimeSpan.FromMilliseconds(1));
            await ScanAndReconcileAsync(forwards);
            Assert.Equal(started + 1, _tunnels.Started.Count);
        }
    }

    [Fact]
    public async Task A_tunnel_that_stayed_up_a_while_restarts_after_the_first_backoff_again()
    {
        var forwards = For();
        forwards.Pin(5173, null);
        await ScanAndReconcileAsync(forwards);
        _tunnels.Started.Last().Exit(255, "gone");
        await ScanAndReconcileAsync(forwards);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await ScanAndReconcileAsync(forwards);

        _clock.Advance(TimeSpan.FromMinutes(5));
        _tunnels.Started.Last().Exit(255, "gone");
        await ScanAndReconcileAsync(forwards);

        Assert.Contains("restarting in 5s", forwards.Row(5173).Error);
    }

    [Fact]
    public async Task A_restart_that_cannot_connect_keeps_retrying_instead_of_failing()
    {
        var forwards = For();
        forwards.Pin(5173, null);
        await ScanAndReconcileAsync(forwards);
        _tunnels.Exits = _ => (255, "ssh: connect to host box port 22: Connection timed out");
        _tunnels.Started.Last().Exit(255, "Connection reset by peer");
        await ScanAndReconcileAsync(forwards);

        _clock.Advance(TimeSpan.FromSeconds(5));
        await ScanAndReconcileAsync(forwards);

        var row = forwards.Row(5173);
        Assert.Equal(ForwardState.Waiting, row.State);
        Assert.Contains("Connection timed out", row.Error);
        Assert.Contains("restarting in 10s", row.Error);

        _tunnels.Exits = _ => null;
        _clock.Advance(TimeSpan.FromSeconds(10));
        await ScanAndReconcileAsync(forwards);
        Assert.Equal(ForwardState.Forwarded, forwards.Row(5173).State);
    }

    [Fact]
    public async Task Forwarding_again_by_hand_skips_the_backoff()
    {
        var forwards = For();
        forwards.Pin(5173, null);
        await ScanAndReconcileAsync(forwards);
        _tunnels.Started.Single().Exit(255, "gone");
        await ScanAndReconcileAsync(forwards);

        var row = await forwards.ForwardNowAsync(5173, null, default);

        Assert.Equal(ForwardState.Forwarded, row.State);
        Assert.Equal(2, _tunnels.Started.Count);
    }

    [Fact]
    public async Task Closing_kills_every_tunnel_and_forwards_nothing_after()
    {
        var forwards = For();
        forwards.Pin(5173, null);
        forwards.Pin(9229, null);
        await ScanAndReconcileAsync(forwards);

        forwards.Close();

        Assert.All(_tunnels.Started, t => Assert.True(t.Killed));
        await ScanAndReconcileAsync(forwards);
        Assert.Equal(2, _tunnels.Started.Count);
    }

    [Fact]
    public async Task A_tunnel_that_comes_up_after_the_link_dropped_is_killed_and_frees_its_port()
    {
        var up = new TaskCompletionSource();
        _tunnels.Up = up.Task;
        var forwards = For();
        forwards.Pin(5173, null);
        var forwarding = ScanAndReconcileAsync(forwards);
        await WaitUntil(() => _tunnels.Started.Count == 1);

        forwards.Unlinked();
        up.SetResult();
        await forwarding;

        Assert.True(_tunnels.Started.Single().Killed);
        Assert.False(_locals.Holds(5173));
        Assert.Equal(ForwardState.Waiting, forwards.Row(5173).State);
    }

    [Fact]
    public async Task A_tunnel_that_comes_up_after_close_is_killed()
    {
        var up = new TaskCompletionSource();
        _tunnels.Up = up.Task;
        var forwards = For();
        forwards.Pin(5173, null);
        var forwarding = ScanAndReconcileAsync(forwards);
        await WaitUntil(() => _tunnels.Started.Count == 1);

        forwards.Close();
        up.SetResult();
        await forwarding;

        Assert.True(_tunnels.Started.Single().Killed);
        Assert.False(_locals.Holds(5173));
    }

    [Fact]
    public async Task Forwarding_a_restarting_port_by_hand_that_fails_keeps_restarting_with_the_real_reason()
    {
        var forwards = For();
        forwards.Pin(5173, null);
        await ScanAndReconcileAsync(forwards);
        _tunnels.Started.Single().Exit(255, "gone");
        await ScanAndReconcileAsync(forwards);
        _tunnels.Exits = _ => (255, "red@box: Permission denied (publickey).");

        var row = await forwards.ForwardNowAsync(5173, null, default);

        Assert.Equal(ForwardState.Waiting, row.State);
        Assert.Contains("red@box: Permission denied (publickey).", row.Error);
        Assert.Contains("restarting in 10s", row.Error);
    }

    [Fact]
    public async Task Pinning_a_port_again_after_unpinning_forgets_its_deaths()
    {
        var forwards = For();
        forwards.Pin(5173, null);
        await ScanAndReconcileAsync(forwards);
        _tunnels.Started.Single().Exit(255, "gone");
        await ScanAndReconcileAsync(forwards);
        await forwards.UnforwardNowAsync(5173, default);
        _tunnels.Exits = _ => (255, "red@box: Permission denied (publickey).");

        var row = await forwards.ForwardNowAsync(5173, null, default);

        Assert.Equal(ForwardState.Failed, row.State);
        Assert.Equal("red@box: Permission denied (publickey).", row.Error);
    }

    [Fact]
    public async Task Ports_still_queued_when_the_link_drops_start_no_tunnel()
    {
        var up = new TaskCompletionSource();
        _tunnels.Up = up.Task;
        var forwards = For();
        forwards.Pin(5173, null);
        forwards.Pin(9229, null);
        var forwarding = ScanAndReconcileAsync(forwards);
        await WaitUntil(() => _tunnels.Started.Count == 1);

        forwards.Unlinked();
        up.SetResult();
        await forwarding;

        Assert.All(_tunnels.Started, t => Assert.True(t.Killed));
        Assert.DoesNotContain(forwards.Rows(), r => r.State == ForwardState.Forwarded);
    }

    [Fact]
    public async Task A_tunnel_start_that_is_cancelled_kills_ssh_and_frees_its_port()
    {
        _tunnels.Up = new TaskCompletionSource().Task;
        var forwards = For();
        forwards.Pin(5173, null);
        using var stop = new CancellationTokenSource();
        var forwarding = forwards.ReconcileAsync(
            new Dictionary<int, IReadOnlyList<string>>(), new Dictionary<int, string>(), stop.Token);
        await WaitUntil(() => _tunnels.Started.Count == 1);

        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => forwarding);
        Assert.True(_tunnels.Started.Single().Killed);
        Assert.False(_locals.Holds(5173));
    }

    [Fact]
    public async Task A_host_that_ssh_would_read_as_an_option_fails_without_starting_ssh()
    {
        var forwards = new HostForwards(
            "-oProxyCommand=calc", null, _locals, _ssh.RunAsync, _ => { }, new TunnelMode(_tunnels, _tunnels, TimeSpan.FromSeconds(2)));
        forwards.Pin(5173, null);

        await ScanAndReconcileAsync(forwards);

        Assert.Empty(_tunnels.Started);
        Assert.Equal(ForwardState.Failed, forwards.Row(5173).State);
        Assert.Contains("starts with '-'", forwards.Row(5173).Error);
        Assert.False(_locals.Holds(5173));
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

    private sealed class Never : Fleet.Ports.Forwards.IListenerProbe
    {
        public Task<bool> ListeningAsync(int port, CancellationToken ct = default) => Task.FromResult(false);
    }
}
