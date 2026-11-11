using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Hooks;
using Fleet.Shared.Status.Enums;

namespace Fleet.Tests.Features.Agents;

public class AgentActivityTests
{
    [Theory]
    [InlineData(AgentHarness.Opencode, true)]
    [InlineData(AgentHarness.Claude, false)]
    [InlineData(AgentHarness.Nvim, false)]
    [InlineData(AgentHarness.Orchestrator, false)]
    public void Only_a_harness_without_hooks_is_watched_through_its_pane(string harness, bool watched) =>
        Assert.Equal(
            watched,
            AgentActivity.WatchedByPane(new AgentRecord("/wt", "api", "x", harness, "origin/main", false, HostInNvim: true)));

    [Fact]
    public void A_pane_quiet_for_the_window_is_idle_and_new_output_is_working()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
        var t0 = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var window = TimeSpan.FromMinutes(2);

        try
        {
            Assert.Equal(AgentActivity.Working, AgentActivity.Quiet(folder, "> ", t0, window));
            Assert.Equal(AgentActivity.Idle, AgentActivity.Quiet(folder, "> ", t0.AddMinutes(3), window));
            Assert.Equal(AgentActivity.Working, AgentActivity.Quiet(folder, "> hi", t0.AddMinutes(4), window));
            Assert.Equal(string.Empty, AgentActivity.Quiet(folder, "  ", t0.AddMinutes(9), window));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_claude_spinner_line_means_working()
    {
        Assert.Equal(
            AgentActivity.Working,
            AgentActivity.Classify("* Churning... (esc to interrupt)"));
    }

    [Fact]
    public void A_permission_prompt_means_waiting()
    {
        Assert.Equal(
            AgentActivity.Waiting,
            AgentActivity.Classify("Do you want to make this edit?\n1. Yes\n2. No, and tell Claude"));
    }

    [Theory]
    [InlineData(AgentState.Blocked, AgentActivity.Waiting)]
    [InlineData(AgentState.Stalled, AgentActivity.Stalled)]
    [InlineData(AgentState.Working, AgentActivity.Working)]
    [InlineData(AgentState.Idle, AgentActivity.Idle)]
    [InlineData(AgentState.Unknown, "")]
    public void A_hook_state_maps_to_the_activity_the_row_shows(AgentState state, string activity)
    {
        Assert.Equal(activity, AgentActivity.For(state));
    }

    [Fact]
    public void A_hook_stall_stands_only_while_the_pane_shows_the_spinner()
    {
        Assert.Equal(AgentState.Stalled, AgentActivity.Confirmed(AgentState.Stalled, "* Churning... (esc to interrupt)"));
        Assert.Equal(AgentState.Idle, AgentActivity.Confirmed(AgentState.Stalled, "Interrupted by user\n> "));
        Assert.Equal(AgentState.Idle, AgentActivity.Confirmed(AgentState.Stalled, string.Empty));
    }

    [Fact]
    public void A_permission_prompt_dismissed_with_esc_is_idle_but_an_unread_pane_keeps_the_block()
    {
        Assert.Equal(AgentState.Idle, AgentActivity.Confirmed(AgentState.Blocked, "Interrupted by user\n> ", HookStatus.PermissionReason));
        Assert.Equal(AgentState.Blocked, AgentActivity.Confirmed(AgentState.Blocked, string.Empty, HookStatus.PermissionReason));
        Assert.Equal(AgentState.Blocked, AgentActivity.Confirmed(AgentState.Blocked, "Do you want to proceed?\n2. No, and tell Claude", HookStatus.PermissionReason));
    }

    [Fact]
    public void A_block_on_input_is_not_released_by_an_idle_looking_pane()
    {
        Assert.Equal(AgentState.Blocked, AgentActivity.Confirmed(AgentState.Blocked, "Pick a server to connect\n> ", HookStatus.InputReason));
    }

    [Theory]
    [InlineData("Do you want to proceed?\n2. No, and tell Claude")]
    [InlineData("Which option should I take? (waiting for your input)")]
    public void A_hook_stall_with_a_prompt_on_screen_is_blocked_not_idle(string pane)
    {
        Assert.Equal(AgentState.Blocked, AgentActivity.Confirmed(AgentState.Stalled, pane));
    }

    [Fact]
    public void A_hook_block_turns_to_work_once_the_pane_runs_the_approved_tool()
    {
        Assert.Equal(AgentState.Working, AgentActivity.Confirmed(AgentState.Blocked, "* Running... (esc to interrupt)"));
        Assert.Equal(AgentState.Blocked, AgentActivity.Confirmed(AgentState.Blocked, "Do you want to proceed?\n2. No, and tell Claude"));
        Assert.Equal(AgentState.Blocked, AgentActivity.Confirmed(AgentState.Blocked, string.Empty));
    }

    [Theory]
    [InlineData(AgentState.Working)]
    [InlineData(AgentState.Idle)]
    public void Work_and_idle_are_not_checked_against_the_pane(AgentState state)
    {
        Assert.Equal(state, AgentActivity.Confirmed(state, string.Empty));
        Assert.False(AgentActivity.NeedsPane(state));
    }

    [Fact]
    public void Ordinary_pane_text_means_idle()
    {
        Assert.Equal(AgentActivity.Idle, AgentActivity.Classify("~\n~\nsome buffer text"));
    }

    [Fact]
    public void Empty_text_stays_unclassified()
    {
        Assert.Equal(string.Empty, AgentActivity.Classify("  \n  "));
    }
}
