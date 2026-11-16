using Fleet.Features.Mcp.ServeMcp;
using Fleet.Ports.Mcp.Models;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Tests.Features.Mcp;

public sealed class RemoveSubStopTests
{
    [Fact]
    public void Remove_sub_takes_an_optional_stop_flag()
    {
        var stop = McpTools.Find("remove_sub")!.Params.Single(p => p.Name == "stop");

        Assert.Equal("boolean", stop.Type);
        Assert.False(stop.Required);
    }

    [Fact]
    public void The_approval_prompt_for_remove_sub_with_stop_says_it_stops_a_running_sub()
    {
        var prompt = ApprovalPrompt.For(
            HarnessTool.RemoveSub,
            new McpRequest("remove_sub", new Dictionary<string, string> { ["slug"] = "upgrade", ["stop"] = "true" }),
            caller: string.Empty);

        Assert.Contains("— upgrade (stopping it first if it is running)", prompt);
    }
}
