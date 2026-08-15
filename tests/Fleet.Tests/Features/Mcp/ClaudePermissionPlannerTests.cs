using Fleet.Features.Mcp.SyncClaudeConfig;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Mcp;

public sealed class ClaudePermissionPlannerTests
{
    [Fact]
    public void Read_tools_are_allowed_on_the_claude_side_by_default()
    {
        var plan = ClaudePermissionPlanner.Plan(SettingsConfig.Default);

        Assert.Contains("mcp__fleet__list_agents", plan.Allow);
    }

    [Fact]
    public void A_fleet_dialog_ask_is_allowed_on_the_claude_side_so_the_call_reaches_fleets_gate()
    {
        var settings = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Ask)
            .With(HarnessTool.NewAgent, AskChannel.FleetDialog);

        var plan = ClaudePermissionPlanner.Plan(settings);

        Assert.Contains("mcp__fleet__new_agent", plan.Allow);
        Assert.DoesNotContain("mcp__fleet__new_agent", plan.Ask);
    }

    [Fact]
    public void A_claude_permission_ask_is_left_for_claude_to_prompt()
    {
        var settings = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Ask)
            .With(HarnessTool.NewAgent, AskChannel.ClaudePermission);

        var plan = ClaudePermissionPlanner.Plan(settings);

        Assert.Contains("mcp__fleet__new_agent", plan.Ask);
        Assert.DoesNotContain("mcp__fleet__new_agent", plan.Allow);
    }

    [Fact]
    public void A_forbidden_tool_is_denied_on_the_claude_side_too()
    {
        var settings = SettingsConfig.Default.With(HarnessTool.RemoveRepository, ActionPolicy.Forbid);

        var plan = ClaudePermissionPlanner.Plan(settings);

        Assert.Contains("mcp__fleet__remove_repository", plan.Deny);
        Assert.DoesNotContain("mcp__fleet__remove_repository", plan.Allow);
    }

    [Fact]
    public void Delete_worktree_gets_no_rule_of_its_own_because_it_rides_remove_agent()
    {
        var plan = ClaudePermissionPlanner.Plan(SettingsConfig.Default);

        Assert.DoesNotContain("mcp__fleet__delete_worktree", plan.Allow);
        Assert.DoesNotContain("mcp__fleet__delete_worktree", plan.Deny);
        Assert.DoesNotContain("mcp__fleet__delete_worktree", plan.Ask);
    }

    [Fact]
    public void Every_rule_is_namespaced_under_the_fleet_server()
    {
        var plan = ClaudePermissionPlanner.Plan(SettingsConfig.Default);

        Assert.All(
            plan.Allow.Concat(plan.Deny).Concat(plan.Ask),
            id => Assert.StartsWith("mcp__fleet__", id));
    }
}
