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

    [Fact]
    public void The_dashboard_tab_is_never_an_agents_even_when_the_cwd_lines_up()
    {
        Assert.False(AgentPanes.Owns(Pane(FleetTabTitles.Dashboard, Sub.Worktree), Sub));
    }
}
