using Fleet.Features.Mcp.ServeMcp;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Tests.Features.Mcp;

public sealed class McpToolsTests
{
    [Fact]
    public void Every_configurable_tool_is_exposed_over_mcp()
    {
        var exposed = McpTools.All.Select(s => s.Tool).ToHashSet();

        foreach (var tool in SettingsDefaults.Configurable.Where(t => t != HarnessTool.DeleteWorktree))
        {
            Assert.Contains(tool, exposed);
        }
    }

    [Fact]
    public void Deleting_a_worktree_rides_on_remove_agent_rather_than_its_own_tool()
    {
        Assert.DoesNotContain(McpTools.All, s => s.Tool == HarnessTool.DeleteWorktree);

        var remove = McpTools.Find("remove_agent")!;

        Assert.Contains(remove.Params, p => p.Name == "delete_worktree");
    }

    [Fact]
    public void No_tool_is_exposed_twice()
    {
        Assert.Equal(McpTools.All.Count, McpTools.All.Select(s => s.Tool).Distinct().Count());
    }

    [Fact]
    public void Each_spec_names_itself_after_its_tool_id()
    {
        Assert.All(McpTools.All, spec => Assert.Equal(HarnessToolIds.For(spec.Tool), spec.Name));
    }

    [Fact]
    public void The_rule_id_is_namespaced_under_the_server()
    {
        Assert.Equal("mcp__fleet__new_agent", McpTools.RuleId(HarnessTool.NewAgent));
    }

    [Fact]
    public void A_tool_is_found_by_its_id()
    {
        var spec = McpTools.Find("list_agents");

        Assert.NotNull(spec);
        Assert.Equal(HarnessTool.ListAgents, spec!.Tool);
    }

    [Fact]
    public void An_unknown_name_finds_nothing()
    {
        Assert.Null(McpTools.Find("teleport"));
    }

    [Fact]
    public void Agent_tools_require_a_repository_and_a_branch()
    {
        var spec = McpTools.Find("stop_agent")!;

        Assert.Contains(spec.Params, p => p.Name == "repository" && p.Required);
        Assert.Contains(spec.Params, p => p.Name == "branch" && p.Required);
    }

    [Fact]
    public void Report_takes_a_status_and_an_optional_summary()
    {
        var spec = McpTools.Find("report")!;

        Assert.Contains(spec.Params, p => p.Name == "status" && p.Required);
        Assert.Contains(spec.Params, p => p.Name == "summary" && !p.Required);
    }
}
