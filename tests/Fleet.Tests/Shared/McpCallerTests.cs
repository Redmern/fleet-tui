using Fleet.Shared.Mcp;

namespace Fleet.Tests.Shared;

public sealed class McpCallerTests
{
    [Fact]
    public void The_main_orchestrator_is_neither_sub_nor_agent()
    {
        var caller = McpCaller.AtRoot("techweb", "C:/repos/techweb");

        Assert.False(caller.IsSub);
        Assert.False(caller.IsAgent);
    }

    [Fact]
    public void A_slug_caller_is_a_sub_orchestrator()
    {
        var caller = new McpCaller("techweb", "C:/repos/techweb", "add-oauth");

        Assert.True(caller.IsSub);
        Assert.False(caller.IsAgent);
    }

    [Fact]
    public void An_agent_caller_is_an_agent_not_a_sub()
    {
        var caller = new McpCaller(
            "techweb", "C:/repos/techweb", McpCaller.ForAgent("backend", "feature/login"));

        Assert.True(caller.IsAgent);
        Assert.False(caller.IsSub);
        Assert.Equal("backend/feature/login", caller.AgentId);
    }
}
