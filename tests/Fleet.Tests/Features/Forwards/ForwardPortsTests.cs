using Fleet.Features.Forwards.ForwardPorts;
using Fleet.Features.Forwards.ForwardPorts.Enums;
using Fleet.Features.Forwards.ForwardPorts.Models;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Tests.Features.Forwards;

public sealed class ForwardPortsTests
{
    private readonly FakeForwards _forwards = new();
    private readonly FakeBrowser _browser = new();

    private ForwardPortsHandler Handler => new(_forwards, _browser, new Known());

    [Theory]
    [InlineData("box 5173", ForwardVerb.Add, "box", 5173, null, false)]
    [InlineData("box 5173 --local 15173 --open", ForwardVerb.Add, "box", 5173, 15173, true)]
    [InlineData("ls", ForwardVerb.List, null, 0, null, false)]
    [InlineData("", ForwardVerb.List, null, 0, null, false)]
    [InlineData("rm box 5173", ForwardVerb.Remove, "box", 5173, null, false)]
    public void Arguments_parse_into_orders(string line, ForwardVerb verb, string? host, int port, int? local, bool open)
    {
        var order = ForwardOrders.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.True(order.Succeeded);
        Assert.Equal(new ForwardOrder(verb, host, port, local, null, open), order.Value);
    }

    [Fact]
    public void Stacks_parse_with_their_project() =>
        Assert.Equal(
            new ForwardOrder(ForwardVerb.Start, "box", Project: "web", Open: true),
            ForwardOrders.Parse(["start", "box", "web", "--open"]).Value);

    [Theory]
    [InlineData("box notaport")]
    [InlineData("box 70000")]
    [InlineData("box 80 --local x")]
    [InlineData("rm box")]
    public void Bad_arguments_give_the_usage(string line) =>
        Assert.False(ForwardOrders.Parse(line.Split(' ')).Succeeded);

    [Fact]
    public async Task Forwarding_with_open_opens_the_local_url_and_a_nickname_names_the_host()
    {
        var result = await Handler.HandleAsync(new ForwardOrder(ForwardVerb.Add, "lab", 5173, Open: true));

        Assert.True(result.Succeeded);
        Assert.Equal(["add red@lab.example 5173 "], _forwards.Calls);
        Assert.Equal(["http://localhost:5173"], _browser.Opened);
        Assert.EndsWith("http://localhost:5173", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_browser_that_will_not_open_is_an_error_not_a_crash()
    {
        _browser.Fails = "no xdg-open";

        var result = await Handler.HandleAsync(new ForwardOrder(ForwardVerb.Add, "box", 5173, Open: true));

        Assert.False(result.Succeeded);
        Assert.Contains("no xdg-open", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_port_forwarded_by_the_viewing_machine_opens_there()
    {
        var viewer = new PortForward("laptop", 5173, 15173, ForwardState.Forwarded, "web", Viewer: true);

        var result = await Handler.OpenedAsync(viewer, open: true, default);

        Assert.True(result.Succeeded);
        Assert.Equal(["viewer-open 5173"], _forwards.Calls);
        Assert.Empty(_browser.Opened);
    }

    [Fact]
    public async Task Open_finds_the_forward_by_remote_or_local_port()
    {
        _forwards.Rows.Add(new PortForward("box", 5173, 15173, ForwardState.Forwarded, "web"));

        Assert.True((await Handler.HandleAsync(ForwardOrders.Parse(["open", "5173"]).Value)).Succeeded);
        Assert.True((await Handler.HandleAsync(ForwardOrders.Parse(["open", "15173"]).Value)).Succeeded);
        Assert.False((await Handler.HandleAsync(ForwardOrders.Parse(["open", "5173", "other"]).Value)).Succeeded);
        Assert.Equal(["http://localhost:15173", "http://localhost:15173"], _browser.Opened);
    }

    [Fact]
    public async Task The_list_describes_every_row()
    {
        _forwards.Rows.Add(new PortForward("box", 5173, 5173, ForwardState.Forwarded, "web"));
        _forwards.Rows.Add(new PortForward("box", 9229, null, ForwardState.Detected));

        var result = await Handler.HandleAsync(new ForwardOrder(ForwardVerb.List));

        Assert.Equal(
            "box  5173 -> localhost:5173  (web)\nbox  9229 -> listening, not forwarded",
            result.Value);
    }

    [Fact]
    public async Task Starting_and_stopping_a_stack_go_to_the_forwards()
    {
        await Handler.HandleAsync(new ForwardOrder(ForwardVerb.Start, "box", Project: "web"));
        await Handler.HandleAsync(new ForwardOrder(ForwardVerb.Stop, "box", Project: "web"));

        Assert.Equal(["start box web", "stop box web"], _forwards.Calls);
        Assert.Empty(_browser.Opened);
    }

    private sealed class Known : IKnownRemoteStore
    {
        public IReadOnlyList<KnownRemote> Load() => [new KnownRemote("red@lab.example", "lab", default)];

        public void Remember(string host, DateTimeOffset connected)
        {
        }

        public void Rename(string host, string? nickname)
        {
        }

        public void Forget(string host)
        {
        }
    }
}
