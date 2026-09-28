using Fleet.Features.Agents;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public class SubBrowseTests
{
    private static readonly AgentRecord Sub = new(
        "C:/repos/techweb/.fleet/orchestrations/remove-pr-pipeline", string.Empty, "remove-pr-pipeline",
        AgentHarness.Orchestrator, string.Empty, RepositoryWasBare: false);

    private static Pane Pane(string tabTitle, string paneTitle) =>
        new(new PaneId("1"), "w1", "t1", "default", tabTitle, Sub.Worktree, true, paneTitle);

    [Fact]
    public void A_browser_is_recognised_by_the_title_its_own_process_announced()
    {
        Assert.True(SubBrowse.Is(Pane("remove-pr-pipeline", "remove-pr-pipeline files")));
    }

    [Fact]
    public void The_claude_pane_is_not_a_browser_even_when_the_shared_tab_title_says_files()
    {
        Assert.False(SubBrowse.Is(Pane("remove-pr-pipeline files", "\u2733 Claude Code")));
        Assert.False(SubBrowse.Is(Pane("remove-pr-pipeline files", string.Empty)));
    }
}
