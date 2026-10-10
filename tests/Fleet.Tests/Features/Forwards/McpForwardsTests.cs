using Fleet.Features.Forwards.ForwardPorts;
using Fleet.Features.Forwards.ForwardPorts.Enums;
using Fleet.Features.Forwards.ForwardPorts.Models;
using Fleet.Ports.Mcp.Models;

namespace Fleet.Tests.Features.Forwards;

public sealed class McpForwardsTests
{
    private static McpRequest Call(string tool, params (string Key, string Value)[] args) =>
        new(tool, args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public void Forward_port_takes_its_remote_port_and_local_port() =>
        Assert.Equal(
            new ForwardOrder(ForwardVerb.Add, "box", 5173, 15173),
            McpForwards.Order(Call("forward_port", ("remote", "box"), ("port", "5173"), ("local_port", "15173")), ForwardVerb.Add).Value);

    [Theory]
    [InlineData("forward_port", ForwardVerb.Add, "port", "5173")]
    [InlineData("forward_port", ForwardVerb.Add, "remote", "box")]
    [InlineData("start_stack", ForwardVerb.Start, "remote", "box")]
    [InlineData("open_url", ForwardVerb.Open, "remote", "box")]
    public void Missing_arguments_are_named(string tool, ForwardVerb verb, string key, string value)
    {
        var order = McpForwards.Order(Call(tool, (key, value)), verb);

        Assert.False(order.Succeeded);
        Assert.Contains("is required", order.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bad_local_port_is_refused() =>
        Assert.False(McpForwards.Order(Call("forward_port", ("remote", "box"), ("port", "80"), ("local_port", "0")), ForwardVerb.Add).Succeeded);

    [Fact]
    public void Start_stack_with_open_opens() =>
        Assert.Equal(
            new ForwardOrder(ForwardVerb.Start, "box", Project: "web", Open: true),
            McpForwards.Order(Call("start_stack", ("remote", "box"), ("project", "web"), ("open", "true")), ForwardVerb.Start).Value);

    [Fact]
    public void List_forwards_needs_nothing() =>
        Assert.True(McpForwards.Order(Call("list_forwards"), ForwardVerb.List).Succeeded);
}
