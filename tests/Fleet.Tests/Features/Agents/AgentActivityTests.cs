using Fleet.Features.Agents.ListAgents;
using Fleet.Shared.Status.Enums;

namespace Fleet.Tests.Features.Agents;

public class AgentActivityTests
{
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
