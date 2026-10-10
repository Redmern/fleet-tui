using Fleet.Features.Forwards.OpenPort;
using Fleet.Features.Forwards.OpenPort.Enums;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Mux.Exceptions;

namespace Fleet.Tests.Features.Forwards;

public sealed class OpenPortTests
{
    private readonly FakeProbe _probe = new() { Listening = { 5272 } };
    private readonly FakeBrowser _browser = new();
    private readonly FakeForwards _forwards = new();

    private OpenPortHandler Handler() => new(_probe, _forwards, _browser, "red");

    [Theory]
    [InlineData("5272", 5272)]
    [InlineData(" 1 ", 1)]
    [InlineData("65535", 65535)]
    public void A_port_between_1_and_65535_is_accepted(string typed, int port)
    {
        var parsed = OpenPortHandler.Parse(typed);

        Assert.True(parsed.Succeeded);
        Assert.Equal(port, parsed.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("80a")]
    [InlineData("5272.0")]
    [InlineData("localhost:5272")]
    public void Anything_else_is_an_error(string typed)
    {
        var parsed = OpenPortHandler.Parse(typed);

        Assert.False(parsed.Succeeded);
        Assert.Contains("1-65535", parsed.Error);
    }

    [Fact]
    public async Task Without_ssh_in_between_it_opens_localhost_in_this_machines_browser()
    {
        var outcome = await Handler().HandleAsync(5272);

        Assert.Equal(["http://localhost:5272"], _browser.Opened);
        Assert.Equal(OpenPortRoute.Local, outcome.Route);
        Assert.True(outcome.Listening);
        Assert.Empty(outcome.Lines);
    }

    [Fact]
    public async Task A_fleet_viewer_gets_a_viewer_forward_and_nothing_opens_here()
    {
        _forwards.Viewer = new ViewerForward("laptop", null);

        var outcome = await Handler().HandleAsync(5272);

        Assert.Equal(["viewer-forward 5272"], _forwards.Calls);
        Assert.Equal(OpenPortRoute.Viewer, outcome.Route);
        Assert.Empty(_browser.Opened);
        Assert.Empty(outcome.Lines);
    }

    [Fact]
    public async Task A_fleet_viewer_hears_that_nothing_listens_yet()
    {
        _forwards.Viewer = new ViewerForward("laptop", null);

        var outcome = await Handler().HandleAsync(3000);

        Assert.Equal(OpenPortRoute.Viewer, outcome.Route);
        Assert.Contains(outcome.Lines, l => l.Contains("nothing listens on 127.0.0.1:3000", StringComparison.Ordinal));
        Assert.Contains(outcome.Lines, l => l.Contains("laptop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_plain_ssh_viewer_gets_the_ssh_command_to_copy_and_nothing_opens_here()
    {
        _forwards.Viewer = new ViewerForward(null, "10.0.0.5 52000 10.0.0.9 22");

        var outcome = await Handler().HandleAsync(5272);

        Assert.Equal(OpenPortRoute.Ssh, outcome.Route);
        Assert.Equal("ssh -N -L 5272:localhost:5272 red@10.0.0.9", outcome.Command);
        Assert.Contains(outcome.Lines, l => l.Contains("http://localhost:5272", StringComparison.Ordinal));
        Assert.Empty(_browser.Opened);
    }

    [Fact]
    public async Task The_ssh_command_names_a_port_other_than_22()
    {
        _forwards.Viewer = new ViewerForward(null, "fe80::5 52000 fe80::9 2222");

        var outcome = await Handler().HandleAsync(3000);

        Assert.Equal("ssh -N -L 3000:localhost:3000 -p 2222 red@fe80::9", outcome.Command);
        Assert.Contains(outcome.Lines, l => l.Contains("nothing listens on 127.0.0.1:3000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_fleetd_it_opens_here()
    {
        _forwards.ViewerFails = new MuxUnavailableException("fleetd did not answer");

        var outcome = await Handler().HandleAsync(5272);

        Assert.Equal(OpenPortRoute.Local, outcome.Route);
        Assert.Equal(["http://localhost:5272"], _browser.Opened);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void A_missing_or_broken_ssh_connection_gives_no_command(string? connection) =>
        Assert.Null(new ViewerForward(null, connection).SshCommand(5272, "red"));

    [Fact]
    public async Task When_nothing_listens_it_says_so_and_still_opens()
    {
        var outcome = await Handler().HandleAsync(3000);

        Assert.Equal(["http://localhost:3000"], _browser.Opened);
        Assert.False(outcome.Listening);
        Assert.Contains(outcome.Lines, l => l.Contains("nothing listens on 127.0.0.1:3000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_browser_that_will_not_start_is_reported()
    {
        _browser.Fails = "no xdg-open";

        var outcome = await Handler().HandleAsync(5272);

        Assert.Contains(outcome.Lines, l => l.Contains("no xdg-open", StringComparison.Ordinal));
        Assert.Contains(outcome.Lines, l => l.Contains("http://localhost:5272", StringComparison.Ordinal));
    }

    private sealed class FakeProbe : IListenerProbe
    {
        public HashSet<int> Listening { get; } = [];

        public Task<bool> ListeningAsync(int port, CancellationToken ct = default) =>
            Task.FromResult(Listening.Contains(port));
    }
}
