using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Shared;

public sealed class ClaudeLaunchTests
{
    private static readonly ClaudeLaunch Named = new("techweb-api-feat-x", new RoleModel("opus", "high"));

    [Fact]
    public void Inherit_emits_no_model_or_effort_flag()
    {
        Assert.Empty(RoleModel.Inherit.Arguments);
        Assert.Equal(["--name", "n"], new ClaudeLaunch("n", RoleModel.Inherit).Arguments);
    }

    [Fact]
    public void A_model_and_an_effort_each_emit_their_flag()
    {
        Assert.Equal(["--model", "sonnet"], new RoleModel("sonnet", ModelChoice.Inherit).Arguments);
        Assert.Equal(["--effort", "max"], new RoleModel(ModelChoice.Inherit, "max").Arguments);
        Assert.Equal(
            ["--name", "techweb-api-feat-x", "--model", "opus", "--effort", "high"],
            Named.Arguments);
    }

    [Theory]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("opus[1m]")]
    [InlineData("us.anthropic.claude-opus-5-5-v1:0")]
    [InlineData("claude-sonnet-4@20250514")]
    public void Model_ids_and_aliases_pass_through(string model)
    {
        Assert.Equal(model, ModelChoice.Model(model));
    }

    [Theory]
    [InlineData("")]
    [InlineData("inherit")]
    [InlineData("INHERIT")]
    [InlineData("opus'; os.exit()")]
    [InlineData("two words")]
    [InlineData("-dash-first")]
    public void A_blank_inherit_or_unsafe_model_means_inherit(string model)
    {
        Assert.Equal(ModelChoice.Inherit, ModelChoice.Model(model));
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("Medium", "medium")]
    [InlineData("xhigh", "xhigh")]
    [InlineData("max", "max")]
    [InlineData("extreme", "inherit")]
    [InlineData("", "inherit")]
    public void Only_known_effort_levels_are_passed(string effort, string expected)
    {
        Assert.Equal(expected, ModelChoice.Effort(effort));
    }

    [Fact]
    public void The_sub_orchestrator_default_is_sonnet_at_medium_and_the_rest_inherit()
    {
        Assert.Equal(new RoleModel("sonnet", "medium"), SettingsDefaults.Models.Sub);
        Assert.Equal(RoleModel.Inherit, SettingsDefaults.Models.Main);
        Assert.Equal(RoleModel.Inherit, SettingsDefaults.Models.Agent);
        Assert.Equal(RoleModel.Inherit, SettingsDefaults.HeadModel);
    }

    [Fact]
    public void Each_role_takes_its_own_model()
    {
        var models = new RoleModels(new("opus", "high"), new("haiku", "low"), new("sonnet", "medium"));

        Assert.Equal(
            ["--name", "techweb-main", "--model", "opus", "--effort", "high"],
            ClaudeLaunch.MainOrchestrator("techweb", models).Arguments);
        Assert.Equal(
            ["--name", "techweb-sub-slug", "--model", "haiku", "--effort", "low"],
            ClaudeLaunch.ForAgent("techweb", string.Empty, "slug", orchestrator: true, models).Arguments);
        Assert.Equal(
            ["--name", "techweb-api-feat-x", "--model", "sonnet", "--effort", "medium"],
            ClaudeLaunch.ForAgent("techweb", "api", "feat/x", orchestrator: false, models).Arguments);
        Assert.Equal(["--name", "fleet-head"], ClaudeLaunch.Head(RoleModel.Inherit).Arguments);
    }

    [Fact]
    public void Every_launch_shape_carries_the_name()
    {
        Assert.Equal(
            [AgentHarness.Claude, "--name", "techweb-api-feat-x", "--model", "opus", "--effort", "high"],
            AgentHarness.CommandFor(AgentHarness.Claude, launch: Named));
        Assert.Equal(
            [AgentHarness.Claude, AgentHarness.ResumeArgument, "--name", "techweb-api-feat-x", "--model", "opus", "--effort", "high"],
            AgentHarness.OrchestratorCommand(resume: true, inNvim: false, Named));
        Assert.Contains(
            "vim.cmd('ClaudeCode --name techweb-api-feat-x --model opus --effort high')",
            AgentHarness.OrchestratorCommand(resume: false, inNvim: true, Named)[2],
            StringComparison.Ordinal);
        Assert.Contains(
            "vim.cmd('ClaudeCode --continue --name techweb-api-feat-x --model opus --effort high')",
            AgentHarness.OrchestratorCommand(resume: true, inNvim: true, Named)[2],
            StringComparison.Ordinal);
        Assert.Contains(
            "vim.cmd('ClaudeCode --name techweb-api-feat-x --model opus --effort high')",
            AgentHarness.CommandFor(AgentHarness.Nvim, withClaude: true, launch: Named)[2],
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_nvim_pane_without_claude_ignores_the_launch()
    {
        Assert.Equal(
            AgentHarness.CommandFor(AgentHarness.Nvim),
            AgentHarness.CommandFor(AgentHarness.Nvim, launch: Named));
    }

    [Fact]
    public void Resuming_keeps_the_name_and_the_model()
    {
        Assert.Equal(
            AgentHarness.OrchestratorCommand(resume: true, inNvim: false, Named),
            AgentHarness.Resumed(AgentHarness.OrchestratorCommand(resume: false, inNvim: false, Named)));
        Assert.Equal(
            AgentHarness.OrchestratorCommand(resume: true, inNvim: true, Named),
            AgentHarness.Resumed(AgentHarness.OrchestratorCommand(resume: false, inNvim: true, Named)));
        Assert.Contains(
            "vim.cmd('ClaudeCode --continue --name techweb-api-feat-x --model opus --effort high')",
            AgentHarness.Resumed(AgentHarness.CommandFor(AgentHarness.Nvim, withClaude: true, launch: Named))[2],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resuming_twice_adds_continue_once()
    {
        var resumed = AgentHarness.Resumed(AgentHarness.CommandFor(AgentHarness.Claude, launch: Named));
        var nvim = AgentHarness.Resumed(AgentHarness.OrchestratorCommand(resume: false, launch: Named));

        Assert.Same(resumed, AgentHarness.Resumed(resumed));
        Assert.Same(nvim, AgentHarness.Resumed(nvim));
        Assert.Single(resumed, a => a == AgentHarness.ResumeArgument);
    }

    [Fact]
    public void Subagent_guidance_is_appended_only_for_a_repo_agent_whose_worktree_has_the_file()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

        try
        {
            Assert.False(ClaudeLaunch.ForAgent("p", "api", "b", false, SettingsDefaults.Models, worktree).SubagentGuidance);

            SubagentGuidance.Apply(worktree, on: true);

            Assert.True(ClaudeLaunch.ForAgent("p", "api", "b", false, SettingsDefaults.Models, worktree).SubagentGuidance);
            Assert.False(ClaudeLaunch.ForAgent("p", string.Empty, "b", true, SettingsDefaults.Models, worktree).SubagentGuidance);
            Assert.False(ClaudeLaunch.ForAgent("p", "api", "b", false, SettingsDefaults.Models).SubagentGuidance);

            SubagentGuidance.Apply(worktree, on: false);

            Assert.False(SubagentGuidance.IsIn(worktree));
        }
        finally
        {
            if (Directory.Exists(worktree))
            {
                Directory.Delete(worktree, recursive: true);
            }
        }
    }

    [Fact]
    public void The_guidance_names_only_built_in_subagents()
    {
        foreach (var personal in new[] { "quick", "worker", "deep", "reviewer" })
        {
            Assert.DoesNotContain($"`{personal}`", SubagentGuidance.ForRepoAgents, StringComparison.Ordinal);
            Assert.DoesNotContain($"`{personal}`", SubagentGuidance.ForSubOrchestrators, StringComparison.Ordinal);
        }

        Assert.Contains("Explore", SubagentGuidance.ForRepoAgents, StringComparison.Ordinal);
        Assert.Contains("isolation: worktree", SubagentGuidance.ForRepoAgents, StringComparison.Ordinal);
    }

    [Fact]
    public void The_repo_agent_guidance_points_at_the_fleet_skills()
    {
        Assert.Contains(
            "fl-tdd, fl-review and fl-pr skills in `.claude/skills`",
            SubagentGuidance.ForRepoAgents,
            StringComparison.Ordinal);
    }
}
