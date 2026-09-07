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

    [Fact]
    public async Task Splitting_a_browser_names_its_pane_and_leaves_the_tab_named_after_the_sub()
    {
        var mux = new FakeMuxDriver();
        var claude = await mux.SpawnAsync(new SpawnOptions { Cwd = Sub.Worktree, NewWindow = true });
        await mux.SetTitleAsync(claude, AgentTitle.For(Sub.Repository, Sub.Branch));

        await SubBrowse.SplitAsync(mux, Sub, claude);

        var panes = await mux.ListPanesAsync();
        var browser = panes.Single(p => p.Id != claude);

        Assert.Equal(claude, panes.Single(p => !SubBrowse.Is(p)).Id);
        Assert.Equal(AgentHarness.BrowseCommandFor("remove-pr-pipeline files"), mux.ArgsFor(browser.Id));
        Assert.Equal("remove-pr-pipeline files", browser.PaneTitle);
        Assert.Equal("remove-pr-pipeline", browser.Title);
        Assert.Equal("remove-pr-pipeline", mux.TitleOf(claude));
        Assert.Equal(Sub.Worktree, browser.Cwd);
    }
}
