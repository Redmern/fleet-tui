using Fleet.Features.Head.ServeHead;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Models;
using Fleet.Tests.Features.Forwards;

namespace Fleet.Tests.Features.Head;

public sealed class HeadForwardsTests
{
    private readonly FakeForwards _forwards = new();
    private readonly FakeBrowser _browser = new();

    private HeadService Service() => new(new HeadDeps(
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        (_, _) => Task.FromResult(false),
        _ => Task.FromResult<string?>(null),
        _ => null,
        null!,
        null!,
        new Known(),
        (_, _) => Task.FromResult(new ProjectStructure([], string.Empty, string.Empty)),
        Forwards: _forwards,
        Browser: _browser,
        User: "red"));

    private static McpRequest Call(string tool, params (string Key, string Value)[] args) =>
        new(tool, args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public async Task Forward_port_resolves_the_nickname_and_answers_with_the_url()
    {
        var result = await Service().HandleAsync(Call(HeadTools.ForwardPort, ("remote", "lab"), ("port", "5173")));

        Assert.False(result.IsError);
        Assert.Equal(["add red@lab.example 5173 "], _forwards.Calls);
        Assert.Contains("http://localhost:5173", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forward_tools_refuse_bad_ports()
    {
        Assert.True((await Service().HandleAsync(Call(HeadTools.ForwardPort, ("remote", "lab"), ("port", "http")))).IsError);
        Assert.True((await Service().HandleAsync(Call(HeadTools.ForwardPort, ("port", "http")))).IsError);
        Assert.Empty(_forwards.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("local")]
    public async Task Forward_port_on_this_machine_goes_to_the_machine_viewing_it(string? remote)
    {
        _forwards.Viewer = new ViewerForward("laptop", null);
        (string, string)[] args = remote is null ? [("port", "5173")] : [("remote", remote), ("port", "5173")];

        var forwarded = await Service().HandleAsync(Call(HeadTools.ForwardPort, args));
        var unforwarded = await Service().HandleAsync(Call(HeadTools.UnforwardPort, args));

        Assert.False(forwarded.IsError, forwarded.Text);
        Assert.False(unforwarded.IsError, unforwarded.Text);
        Assert.Equal(["viewer-forward 5173", "viewer-unforward 5173"], _forwards.Calls);
    }

    [Fact]
    public async Task Forward_port_on_this_machine_reached_by_plain_ssh_names_the_ssh_command()
    {
        _forwards.Viewer = new ViewerForward(null, "10.0.0.5 52000 10.0.0.9 22");

        var result = await Service().HandleAsync(Call(HeadTools.ForwardPort, ("port", "5173")));

        Assert.True(result.IsError);
        Assert.Contains("ssh -N -L 5173:localhost:5173 red@10.0.0.9", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forward_port_on_this_machine_without_a_viewer_is_an_error()
    {
        var result = await Service().HandleAsync(Call(HeadTools.ForwardPort, ("port", "5173")));

        Assert.True(result.IsError);
        Assert.Contains("no machine views this one", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_url_opens_the_forward_of_that_port()
    {
        _forwards.Rows.Add(new PortForward("red@lab.example", 5173, 15173, ForwardState.Forwarded, "web"));

        var result = await Service().HandleAsync(Call(HeadTools.OpenUrl, ("port", "5173")));

        Assert.False(result.IsError);
        Assert.Equal(["http://localhost:15173"], _browser.Opened);
    }

    [Fact]
    public async Task Open_url_on_a_port_that_is_not_forwarded_says_so()
    {
        _forwards.Rows.Add(new PortForward("red@lab.example", 9229, null, ForwardState.Detected));

        var result = await Service().HandleAsync(Call(HeadTools.OpenUrl, ("port", "9229")));

        Assert.True(result.IsError);
        Assert.Empty(_browser.Opened);
    }

    [Fact]
    public async Task Start_stack_with_open_opens_the_browser_and_stop_stack_stops_it()
    {
        var started = await Service().HandleAsync(Call(HeadTools.StartStack, ("remote", "lab"), ("project", "web"), ("open", "true")));
        var stopped = await Service().HandleAsync(Call(HeadTools.StopStack, ("remote", "lab"), ("project", "web")));

        Assert.False(started.IsError);
        Assert.False(stopped.IsError);
        Assert.Equal(["start red@lab.example web", "stop red@lab.example web"], _forwards.Calls);
        Assert.Equal(["http://localhost:5173"], _browser.Opened);
    }

    [Fact]
    public async Task List_forwards_lists_every_row()
    {
        _forwards.Rows.Add(new PortForward("red@lab.example", 5173, 5173, ForwardState.Forwarded, "web"));

        var result = await Service().HandleAsync(Call(HeadTools.ListForwards));

        Assert.Equal("red@lab.example  5173 -> localhost:5173  (web)  http://localhost:5173", result.Text);
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
