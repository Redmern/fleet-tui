using Fleet.Features.Mcp.ServeMcp;
using Fleet.Features.Orchestrations.WaitForAgent;
using Fleet.Shared.Harness;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Tests.Features.Mcp;

public sealed class McpToolsTests
{
    [Fact]
    public void Every_configurable_tool_is_exposed_over_mcp()
    {
        var exposed = McpTools.All.Select(s => s.Tool).ToHashSet();

        foreach (var tool in SettingsDefaults.Configurable.Where(SettingsDefaults.ServedByProjectMcp))
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
    public void The_wait_tool_describes_its_default_and_the_cap_of_every_harness()
    {
        var timeout = McpTools.Find("wait_for_agent")!.Params.Single(p => p.Name == "timeout").Description;

        Assert.Contains($"default {(int)WaitForAgentHandler.DefaultTimeout.TotalSeconds},", timeout);
        Assert.Contains("clamped", timeout);

        foreach (var profile in HarnessRegistry.Profiles)
        {
            Assert.Matches($@"{(int)profile.WaitCap.TotalSeconds} s for [^;]*\b{profile.Name}\b", timeout);
        }
    }

    [Theory]
    [InlineData("new_agent")]
    [InlineData("dispatch")]
    public void The_tools_that_start_a_repository_agent_take_an_optional_base(string tool)
    {
        var @base = McpTools.Find(tool)!.Params.Single(p => p.Name == "base");

        Assert.False(@base.Required);
        Assert.Equal("string", @base.Type);
        Assert.Contains("origin/", @base.Description);
        Assert.Contains("default branch", @base.Description);
    }

    [Fact]
    public void Report_takes_optional_verify_fields_and_says_fleet_captures_the_git_state()
    {
        var report = McpTools.Find("report")!;

        Assert.Equal("string", report.Params.Single(p => p.Name == "verify_command").Type);
        Assert.Equal("integer", report.Params.Single(p => p.Name == "verify_exit_code").Type);
        Assert.Equal("string", report.Params.Single(p => p.Name == "verify_output").Type);
        Assert.All(report.Params.Where(p => p.Name.StartsWith("verify_")), p => Assert.False(p.Required));
        Assert.Contains("40 lines", report.Params.Single(p => p.Name == "verify_output").Description);
        Assert.Contains("HEAD SHA", report.Description);
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
    public void New_agent_can_take_an_optional_first_task()
    {
        var spec = McpTools.Find("new_agent")!;

        Assert.Contains(spec.Params, p => p.Name == "task" && !p.Required);
    }

    [Fact]
    public void Tell_agent_sends_a_required_message_to_a_named_agent()
    {
        var spec = McpTools.Find("tell_agent")!;

        Assert.Contains(spec.Params, p => p.Name == "repository" && p.Required);
        Assert.Contains(spec.Params, p => p.Name == "branch" && p.Required);
        Assert.Contains(spec.Params, p => p.Name == "message" && p.Required);
    }

    [Fact]
    public void Report_takes_a_status_and_an_optional_summary()
    {
        var spec = McpTools.Find("report")!;

        Assert.Contains(spec.Params, p => p.Name == "status" && p.Required);
        Assert.Contains(spec.Params, p => p.Name == "summary" && !p.Required);
    }

    [Fact]
    public void Dispatch_takes_a_required_message_and_an_optional_aidlc_profile()
    {
        var spec = McpTools.Find("dispatch")!;

        Assert.Contains(spec.Params, p => p.Name == "message" && p.Required);
        Assert.Contains(spec.Params, p => p.Name == "profile" && !p.Required && p.Description.Contains("feature", StringComparison.Ordinal));
    }

    [Fact]
    public void Dispatch_takes_an_optional_harness_naming_every_registered_one()
    {
        var harness = Assert.Single(McpTools.Find("dispatch")!.Params, p => p.Name == "harness");

        Assert.False(harness.Required);
        Assert.All(
            Fleet.Shared.Harness.HarnessRegistry.Profiles,
            p => Assert.Contains(p.Name, harness.Description, StringComparison.Ordinal));
        Assert.Contains("claude", harness.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Todo_add_takes_a_required_title_and_an_optional_body_and_effort()
    {
        var spec = McpTools.Find("todo_add")!;

        Assert.Equal(HarnessTool.TodoAdd, spec.Tool);
        Assert.True(spec.Params.Single(p => p.Name == "title").Required);
        Assert.False(spec.Params.Single(p => p.Name == "body").Required);
        Assert.False(spec.Params.Single(p => p.Name == "effort").Required);
    }

    [Fact]
    public void Remove_sub_takes_a_slug_and_two_optional_flags()
    {
        var remove = McpTools.Find("remove_sub")!;

        Assert.Equal(HarnessTool.RemoveSub, remove.Tool);
        Assert.True(remove.Params.Single(p => p.Name == "slug").Required);
        Assert.False(remove.Params.Single(p => p.Name == "delete_folder").Required);
        Assert.False(remove.Params.Single(p => p.Name == "remove_agents").Required);
    }

    [Fact]
    public void The_sub_tools_are_exposed_by_their_ids()
    {
        Assert.Equal(HarnessTool.ListSubs, McpTools.Find("list_subs")!.Tool);
        Assert.Equal(HarnessTool.StopSub, McpTools.Find("stop_sub")!.Tool);
        Assert.Equal(HarnessTool.RemoveSub, McpTools.Find("remove_sub")!.Tool);
    }
}
