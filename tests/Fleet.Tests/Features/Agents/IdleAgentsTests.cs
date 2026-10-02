using Fleet.Features.Agents.AutoClose;
using Fleet.Features.Agents.AutoClose.Models;
using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Agents;

public sealed class IdleAgentsTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string ProjectRoot = Path.Combine(Path.GetTempPath(), "techweb");

    private static readonly SettingsConfig On = SettingsConfig.Default.WithAutoClose(true, 30);

    private static AgentRecord Agent(string status = OrchestrationStatus.Done, string harness = AgentHarness.Claude) =>
        new(Path.Combine(ProjectRoot, "backend", "feature_login"), "backend", "feature/login", harness, "origin/main", false,
            Open: true, Status: status);

    private static IdleWatch Idle(
        AgentRecord? agent = null,
        TimeSpan? idleFor = null,
        bool alive = true,
        bool focused = false,
        bool asks = false,
        string text = "> \n  ? for shortcuts") =>
        new(
            agent ?? Agent(),
            alive,
            focused,
            Working: AgentActivity.Classify(text) == AgentActivity.Working,
            AsksTheUser: asks || AgentActivity.Classify(text) == AgentActivity.Waiting,
            Now - (idleFor ?? TimeSpan.FromMinutes(31)));

    [Fact]
    public void A_done_agent_idle_past_the_threshold_is_closed()
    {
        Assert.True(IdleAgents.ShouldClose(Idle(), On, ProjectRoot, Now));
    }

    [Fact]
    public void A_failed_agent_idle_past_the_threshold_is_closed()
    {
        Assert.True(IdleAgents.ShouldClose(Idle(Agent(OrchestrationStatus.Failed)), On, ProjectRoot, Now));
    }

    [Fact]
    public void A_done_sub_orchestrator_is_closed_too()
    {
        Assert.True(IdleAgents.ShouldClose(Idle(Agent(harness: AgentHarness.Orchestrator)), On, ProjectRoot, Now));
    }

    [Fact]
    public void Nothing_closes_while_the_setting_is_off()
    {
        Assert.False(IdleAgents.ShouldClose(Idle(idleFor: TimeSpan.FromDays(1)), SettingsConfig.Default, ProjectRoot, Now));
    }

    [Fact]
    public void Exactly_the_threshold_counts_as_idle()
    {
        Assert.True(IdleAgents.ShouldClose(Idle(idleFor: TimeSpan.FromMinutes(30)), On, ProjectRoot, Now));
    }

    [Fact]
    public void Just_under_the_threshold_stays_open()
    {
        Assert.False(IdleAgents.ShouldClose(
            Idle(idleFor: TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1)), On, ProjectRoot, Now));
    }

    [Fact]
    public void The_threshold_comes_from_the_project_setting()
    {
        var hour = SettingsConfig.Default.WithAutoClose(true, 60);

        Assert.False(IdleAgents.ShouldClose(Idle(idleFor: TimeSpan.FromMinutes(59)), hour, ProjectRoot, Now));
        Assert.True(IdleAgents.ShouldClose(Idle(idleFor: TimeSpan.FromMinutes(60)), hour, ProjectRoot, Now));
    }

    [Fact]
    public void A_threshold_below_one_minute_never_closes()
    {
        Assert.False(IdleAgents.ShouldClose(Idle(), SettingsConfig.Default.WithAutoClose(true, 0), ProjectRoot, Now));
    }

    [Theory]
    [InlineData(OrchestrationStatus.Working)]
    [InlineData("")]
    public void An_agent_that_has_not_reported_done_or_failed_stays_open(string status)
    {
        Assert.False(IdleAgents.ShouldClose(Idle(Agent(status)), On, ProjectRoot, Now));
    }

    [Fact]
    public void An_agent_that_is_working_again_after_reporting_done_stays_open()
    {
        Assert.False(IdleAgents.ShouldClose(Idle(text: "✻ Thinking… (esc to interrupt)"), On, ProjectRoot, Now));
    }

    [Theory]
    [InlineData("Do you want to proceed?\n❯ 1. Yes\n  3. No, and tell Claude what to do differently")]
    [InlineData("Claude is waiting for your input")]
    public void An_agent_waiting_for_the_user_stays_open(string text)
    {
        Assert.False(IdleAgents.ShouldClose(Idle(text: text), On, ProjectRoot, Now));
    }

    [Fact]
    public void An_agent_with_an_open_permission_or_question_notice_stays_open()
    {
        Assert.False(IdleAgents.ShouldClose(Idle(asks: true), On, ProjectRoot, Now));
    }

    [Fact]
    public void The_focused_pane_stays_open()
    {
        Assert.False(IdleAgents.ShouldClose(Idle(focused: true), On, ProjectRoot, Now));
    }

    [Fact]
    public void A_hidden_agent_that_is_still_working_stays_open()
    {
        var hidden = Agent(OrchestrationStatus.Working) with { Hidden = true };

        Assert.False(IdleAgents.ShouldClose(Idle(hidden), On, ProjectRoot, Now));
    }

    [Fact]
    public void A_hidden_agent_that_is_done_and_idle_is_closed()
    {
        Assert.True(IdleAgents.ShouldClose(Idle(Agent() with { Hidden = true }), On, ProjectRoot, Now));
    }

    [Fact]
    public void The_main_orchestrator_in_the_project_root_is_never_closed()
    {
        var main = Agent(harness: AgentHarness.Orchestrator) with { Worktree = ProjectRoot + Path.DirectorySeparatorChar };

        Assert.False(IdleAgents.ShouldClose(Idle(main), On, ProjectRoot, Now));
    }

    [Fact]
    public void An_agent_without_a_pane_has_nothing_to_close()
    {
        Assert.False(IdleAgents.ShouldClose(Idle(alive: false), On, ProjectRoot, Now));
    }

    private static Pane PaneAt(string id, string cwd, bool active, string paneTitle = "", string workspace = "techweb") =>
        new(new PaneId(id), workspace, "t" + id, workspace, string.Empty, cwd, active, paneTitle);

    [Fact]
    public void An_agents_editor_pane_is_not_counted_as_the_agents_pane()
    {
        var agent = Agent();
        var editor = PaneAt("2", agent.Worktree, active: true, AgentPaneMatch.EditorTitle(agent));

        Assert.Empty(IdleAgents.PanesOf(agent, [editor]));
    }

    [Fact]
    public void Focus_in_the_editor_pane_does_not_keep_the_agent_open()
    {
        var agent = Agent();
        var panes = new[]
        {
            PaneAt("1", agent.Worktree, active: false),
            PaneAt("2", agent.Worktree, active: true, AgentPaneMatch.EditorTitle(agent)),
        };

        var owned = IdleAgents.PanesOf(agent, panes);

        Assert.Equal("1", Assert.Single(owned).Id.Value);
        Assert.False(IdleAgents.Focused(owned));
    }

    [Fact]
    public void The_active_agent_pane_counts_as_focused()
    {
        var agent = Agent();

        Assert.True(IdleAgents.Focused(IdleAgents.PanesOf(agent, [PaneAt("1", agent.Worktree, active: true)])));
    }

    [Fact]
    public void The_active_pane_of_the_hidden_workspace_is_not_focused()
    {
        var agent = Agent();
        var hidden = PaneAt("1", agent.Worktree, active: true, workspace: FleetWorkspaces.Hidden);

        Assert.False(IdleAgents.Focused(IdleAgents.PanesOf(agent, [hidden])));
    }

    [Fact]
    public void The_log_line_says_why_the_pane_went()
    {
        Assert.Equal(
            "auto-closed backend/feature-x: done and idle for 42 min; open it again to continue",
            IdleAgents.Note("backend/feature-x", "DONE", TimeSpan.FromMinutes(42.7)));
    }
}
