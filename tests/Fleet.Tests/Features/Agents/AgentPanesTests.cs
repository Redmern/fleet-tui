using Fleet.Features.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public class AgentPanesTests
{
    private static readonly AgentRecord Sub = new(
        "C:/repos/techweb/.fleet/orchestrations/remove-pr-pipeline", string.Empty, "remove-pr-pipeline",
        AgentHarness.Orchestrator, string.Empty, RepositoryWasBare: false);

    private static Pane Pane(string title, string cwd) =>
        new(new PaneId("2"), "w1", "t1", "default", title, cwd, true);

    [Fact]
    public void A_pane_at_the_worktree_belongs_to_the_agent()
    {
        Assert.True(AgentPanes.Owns(Pane(string.Empty, Sub.Worktree), Sub));
    }

    [Fact]
    public void A_pane_in_a_tab_named_after_the_agent_belongs_to_it_whatever_its_cwd_says()
    {
        Assert.True(AgentPanes.Owns(Pane("remove-pr-pipeline", string.Empty), Sub));
    }

    [Theory]
    [InlineData("techweb", true)]
    [InlineData(FleetWorkspaces.Hidden, false)]
    [InlineData("techweb" + FleetWorkspaces.HiddenSuffix, false)]
    public void An_agent_is_shown_unless_its_panes_sit_in_a_hidden_workspace(string workspace, bool shown)
    {
        var pane = new Pane(new PaneId("2"), workspace, "t1", workspace, string.Empty, Sub.Worktree, true);

        Assert.Equal(shown, AgentPanes.Shown(Sub, [pane]));
    }

    [Fact]
    public void An_agent_without_panes_is_not_shown()
    {
        Assert.False(AgentPanes.Shown(Sub, []));
    }

    [Fact]
    public void The_dashboard_tab_is_never_an_agents_even_when_the_cwd_lines_up()
    {
        Assert.False(AgentPanes.Owns(Pane(FleetTabTitles.Dashboard, Sub.Worktree), Sub));
    }
}
