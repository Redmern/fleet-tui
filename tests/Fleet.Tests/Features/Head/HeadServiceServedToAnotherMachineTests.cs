using Fleet.Features.Head.ServeHead;

namespace Fleet.Tests.Features.Head;

public sealed partial class HeadServiceTests
{
    [Theory]
    [InlineData(HeadTools.ListAgents)]
    [InlineData(HeadTools.Tell)]
    [InlineData(HeadTools.Relay)]
    [InlineData(HeadTools.ProjectStructure)]
    [InlineData(HeadTools.MenuAction)]
    public async Task A_head_tool_that_arrives_over_a_link_naming_another_machine_is_refused_and_reaches_no_remote(string tool)
    {
        var result = await Service().ServeOriginAsync(Call(tool, (HeadTools.Remote, "red@third")));

        Assert.True(result.IsError);
        Assert.Contains("acts on its own projects only", result.Text, StringComparison.Ordinal);
        Assert.Empty(_remotes.Forwarded);
        Assert.Empty(_remotes.Connected);
    }
}
