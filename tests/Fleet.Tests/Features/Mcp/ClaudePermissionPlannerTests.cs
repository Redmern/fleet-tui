using Fleet.Features.Mcp.SyncClaudeConfig;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Mcp;

public sealed class ClaudePermissionPlannerTests
{
    [Fact]
    public void In_iso_mode_push_and_merge_are_denied_whatever_the_project_says()
    {
        var settings = SettingsConfig.Default.WithPush(ActionPolicy.Allow).WithMerge(ActionPolicy.Allow);

        var plan = ClaudePermissionPlanner.Plan(settings, iso: true);

        Assert.Contains(GitGates.PushRule, plan.Deny);
        Assert.Contains(GitGates.MergeRule, plan.Deny);
        Assert.Contains(GitGates.MergePowerShellRule, plan.Deny);
        Assert.DoesNotContain(GitGates.PushRule, plan.Allow);
        Assert.Contains(GitGates.PrCreateRule, plan.Deny);
    }

    [Fact]
    public void Without_iso_mode_creating_a_pr_is_left_alone() =>
        Assert.DoesNotContain(GitGates.PrCreateRule, ClaudePermissionPlanner.Plan(SettingsConfig.Default).Deny);

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
    public void Every_rule_is_a_fleet_tool_or_a_known_git_gate()
    {
        var plan = ClaudePermissionPlanner.Plan(SettingsConfig.Default);

        Assert.All(
            plan.Allow.Concat(plan.Deny).Concat(plan.Ask),
            id => Assert.True(
                id.StartsWith("mcp__fleet__", StringComparison.Ordinal) || GitGates.IsOwned(id),
                id));
    }

    [Fact]
    public void Committing_and_pushing_ask_by_default()
    {
        var plan = ClaudePermissionPlanner.Plan(SettingsConfig.Default);

        Assert.Contains(GitGates.CommitRule, plan.Ask);
        Assert.Contains(GitGates.PushRule, plan.Ask);
    }

    [Fact]
    public void Auto_commit_is_allowed_and_forbidding_push_denies_it()
    {
        var settings = SettingsConfig.Default
            .WithCommit(ActionPolicy.Allow)
            .WithPush(ActionPolicy.Forbid);

        var plan = ClaudePermissionPlanner.Plan(settings);

        Assert.Contains(GitGates.CommitRule, plan.Allow);
        Assert.DoesNotContain(GitGates.CommitRule, plan.Ask);
        Assert.Contains(GitGates.PushRule, plan.Deny);
        Assert.DoesNotContain(GitGates.PushRule, plan.Allow);
    }

    [Fact]
    public void Merging_a_pull_request_is_allowed_by_default_in_bash_and_powershell()
    {
        var plan = ClaudePermissionPlanner.Plan(SettingsConfig.Default);

        Assert.Contains(GitGates.MergeRule, plan.Allow);
        Assert.Contains(GitGates.MergePowerShellRule, plan.Allow);
    }

    [Fact]
    public void Forbidding_merge_denies_it_and_asking_asks()
    {
        var forbidden = ClaudePermissionPlanner.Plan(SettingsConfig.Default.WithMerge(ActionPolicy.Forbid));
        var asked = ClaudePermissionPlanner.Plan(SettingsConfig.Default.WithMerge(ActionPolicy.Ask));

        Assert.Contains(GitGates.MergeRule, forbidden.Deny);
        Assert.Contains(GitGates.MergePowerShellRule, forbidden.Deny);
        Assert.DoesNotContain(GitGates.MergeRule, forbidden.Allow);
        Assert.Contains(GitGates.MergeRule, asked.Ask);
        Assert.Contains(GitGates.MergePowerShellRule, asked.Ask);
        Assert.DoesNotContain(GitGates.MergeRule, asked.Allow);
    }
}
