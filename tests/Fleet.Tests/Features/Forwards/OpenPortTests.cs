using Fleet.Features.Forwards.OpenPort;
using Fleet.Ports.Forwards;

namespace Fleet.Tests.Features.Forwards;

public sealed class OpenPortTests
{
    private readonly FakeProbe _probe = new() { Listening = { 5272 } };
    private readonly FakeBrowser _browser = new();

    private OpenPortHandler Handler() => new(_probe, _browser);

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
        Assert.True(outcome.Listening);
        Assert.Empty(outcome.Lines);
    }

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
