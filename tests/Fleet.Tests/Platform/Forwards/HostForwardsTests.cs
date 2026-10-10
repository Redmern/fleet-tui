using Fleet.Platform.Forwards;
using Fleet.Platform.Forwards.Models;
using Fleet.Ports.Forwards.Enums;

namespace Fleet.Tests.Platform.Forwards;

public sealed class HostForwardsTests
{
    private readonly FakeSsh _ssh = new();
    private readonly LocalPorts _locals = new(_ => true, () => 41000);

    private HostForwards For(string host = "box", LocalPorts? locals = null) =>
        new(host, "/run/f/cm-" + host, locals ?? _locals, _ssh.RunAsync, _ => { });

    private static Dictionary<int, IReadOnlyList<string>> Listening(params int[] ports) =>
        ports.ToDictionary(p => p, _ => (IReadOnlyList<string>)["127.0.0.1"]);

    private static Dictionary<int, string> Allowed(params int[] ports) => ports.ToDictionary(p => p, _ => "web");

    [Fact]
    public async Task Only_allowlisted_ports_are_forwarded_and_the_rest_are_listed()
    {
        var forwards = For();

        await forwards.ReconcileAsync(Listening(5173, 9229), Allowed(5173), default);

        Assert.Equal(["127.0.0.1:5173:127.0.0.1:5173"], _ssh.Forwards("forward"));
        var rows = forwards.Rows();
        Assert.Equal(ForwardState.Forwarded, rows.Single(r => r.RemotePort == 5173).State);
        Assert.Equal("http://localhost:5173", rows.Single(r => r.RemotePort == 5173).Url);
        Assert.Equal(ForwardState.Detected, rows.Single(r => r.RemotePort == 9229).State);
    }

    [Fact]
    public async Task A_port_that_stops_listening_is_cancelled()
    {
        var forwards = For();
        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);

        await forwards.ReconcileAsync(Listening(), Allowed(5173), default);

        Assert.Equal(["127.0.0.1:5173:127.0.0.1:5173"], _ssh.Forwards("cancel"));
        Assert.Equal(ForwardState.Waiting, forwards.Row(5173).State);
        Assert.False(_locals.Holds(5173));
    }

    [Fact]
    public async Task A_manual_forward_waits_for_its_port_then_forwards()
    {
        var forwards = For();
        await forwards.ReconcileAsync(Listening(), Allowed(), default);

        var row = await forwards.ForwardNowAsync(9229, null, default);
        Assert.Equal(ForwardState.Waiting, row.State);

        await forwards.ReconcileAsync(Listening(9229), Allowed(), default);
        Assert.Equal(ForwardState.Forwarded, forwards.Row(9229).State);
    }

    [Fact]
    public async Task Removing_an_allowlisted_forward_keeps_it_off_until_the_port_restarts()
    {
        var forwards = For();
        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);

        await forwards.UnforwardNowAsync(5173, default);
        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);
        Assert.Equal(ForwardState.Detected, forwards.Row(5173).State);

        await forwards.ReconcileAsync(Listening(), Allowed(5173), default);
        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);
        Assert.Equal(ForwardState.Forwarded, forwards.Row(5173).State);
    }

    [Fact]
    public async Task After_the_link_drops_forwards_come_back_on_their_old_local_ports()
    {
        var next = 41000;
        var locals = new LocalPorts(p => p != 3000 || next > 41000, () => ++next);
        var forwards = For(locals: locals);
        await forwards.ReconcileAsync(Listening(3000), Allowed(3000), default);
        Assert.Equal(41001, forwards.Row(3000).LocalPort);

        forwards.Unlinked();
        Assert.Equal(ForwardState.Waiting, forwards.Row(3000).State);
        Assert.Equal("the link is down", forwards.Row(3000).Error);

        await forwards.ReconcileAsync(Listening(3000), Allowed(3000), default);
        Assert.Equal(41001, forwards.Row(3000).LocalPort);
        Assert.Equal(2, _ssh.Forwards("forward").Count());
    }

    [Fact]
    public async Task Two_hosts_forwarding_the_same_port_get_different_local_ports()
    {
        var next = 41000;
        var locals = new LocalPorts(_ => true, () => ++next);
        var one = For("one", locals);
        var two = For("two", locals);

        await one.ReconcileAsync(Listening(3000), Allowed(3000), default);
        await two.ReconcileAsync(Listening(3000), Allowed(3000), default);

        Assert.Equal(3000, one.Row(3000).LocalPort);
        Assert.Equal(41001, two.Row(3000).LocalPort);
    }

    [Fact]
    public async Task A_refused_forward_is_failed_with_a_clear_reason_and_frees_its_local_port()
    {
        _ssh.Answer = args => args.Contains("forward")
            ? new SshResult(255, string.Empty, "mux_client_forward: forwarding request failed: administratively prohibited")
            : null;
        var forwards = For();

        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);

        var row = forwards.Row(5173);
        Assert.Equal(ForwardState.Failed, row.State);
        Assert.Equal(HostForwards.ProhibitedError, row.Error);
        Assert.False(_locals.Holds(5173));

        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);
        Assert.Single(_ssh.Forwards("forward"));
    }

    [Fact]
    public async Task A_refusal_is_forgotten_once_the_link_restarts()
    {
        var forwards = For();
        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);
        forwards.Prohibited();
        Assert.Equal(HostForwards.ProhibitedError, forwards.Row(5173).Error);

        forwards.Unlinked();
        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);

        Assert.Null(forwards.Row(5173).Error);
    }

    [Fact]
    public async Task An_app_on_ipv6_loopback_only_is_reached_on_ipv6()
    {
        var forwards = For();

        await forwards.ReconcileAsync(
            new Dictionary<int, IReadOnlyList<string>> { [5173] = ["::1"] },
            Allowed(5173),
            default);

        Assert.Equal(["127.0.0.1:5173:[::1]:5173"], _ssh.Forwards("forward"));
    }

    [Fact]
    public async Task A_scan_runs_the_listing_over_the_master_and_filters_system_ports()
    {
        _ssh.Listening = "LISTEN 0 128 0.0.0.0:22 0.0.0.0:*\nLISTEN 0 511 127.0.0.1:5173 0.0.0.0:*\n";
        var forwards = For();

        var scanned = await forwards.ScanAsync(default);

        Assert.Equal([5173], scanned!.Keys);
        Assert.Equal(ListeningPorts.Command, _ssh.Calls.Single().Last());
    }

    [Fact]
    public async Task Asking_for_another_local_port_moves_an_existing_forward()
    {
        var forwards = For();
        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);

        var moved = await forwards.ForwardNowAsync(5173, 8080, default);

        Assert.Equal(8080, moved.LocalPort);
        Assert.Equal(["127.0.0.1:5173:127.0.0.1:5173"], _ssh.Forwards("cancel"));
    }

    [Fact]
    public async Task A_host_that_had_forwards_stays_wanted_until_it_reconnects()
    {
        var forwards = For();
        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);

        forwards.Unlinked();
        forwards.Unlinked();
        Assert.True(forwards.Wanted);

        await forwards.ReconcileAsync(Listening(), Allowed(), default);
        Assert.False(forwards.Wanted);
    }

    [Fact]
    public async Task Removing_every_forward_while_the_link_is_down_stops_wanting_the_host()
    {
        var forwards = For();
        await forwards.ReconcileAsync(Listening(5173), Allowed(5173), default);
        forwards.Unlinked();

        await forwards.UnforwardNowAsync(5173, default);

        Assert.False(forwards.Wanted);
    }

    [Fact]
    public void Allowlists_of_several_projects_merge_and_the_first_project_names_a_shared_port()
    {
        var allowed = HostForwards.Allowlist([("web", [5173, 3000]), ("api", [3000, 8080])]);

        Assert.Equal("web", allowed[3000]);
        Assert.Equal("api", allowed[8080]);
    }
}
