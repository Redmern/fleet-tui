using Fleet.Features.Dashboard.RebuildDashboard;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Dashboard;

public class RebuildDashboardTests
{
    private const string Root = "C:/repos/techweb";

    private readonly FakeMuxDriver _mux = new();

    private static readonly AgentRecord Sub = new(
        Root + "/.fleet/orchestrations/remove-pr-pipeline", string.Empty, "remove-pr-pipeline",
        AgentHarness.Orchestrator, string.Empty, RepositoryWasBare: false);

    private RebuildDashboardHandler Handler => new(_mux);

    private async Task<(PaneId Harness, PaneId Dash)> ProjectAsync()
    {
        var harness = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = Root, NewWindow = true, Args = [AgentHarness.Claude] });
        await _mux.SetTitleAsync(harness, FleetTabTitles.Dashboard);
        var dash = await _mux.SplitAsync(
            new SplitOptions(harness, SplitDirection.Right) { Cwd = Root, Args = ["fleet", "dash"] });
        _mux.CurrentPane = dash;

        return (harness, dash);
    }

    [Fact]
    public async Task An_intact_layout_is_left_alone()
    {
        var (harness, dash) = await ProjectAsync();

        var result = await Handler.HandleAsync(Root, [Sub], AgentHarness.Orchestrator);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("intact", result.Value);

        var panes = await _mux.ListPanesAsync();
        Assert.Equal(2, panes.Count);
        Assert.Equal(panes.Single(p => p.Id == harness).TabId, panes.Single(p => p.Id == dash).TabId);
    }

    [Fact]
    public async Task A_dashboard_dragged_into_a_subs_tab_is_moved_back_beside_its_harness()
    {
        var (harness, dash) = await ProjectAsync();

        var browser = await _mux.SplitAsync(
            new SplitOptions(dash, SplitDirection.Right)
            {
                Cwd = Sub.Worktree,
                Args = AgentHarness.BrowseCommandFor("remove-pr-pipeline files"),
            });
        await _mux.MovePaneAsync(dash, new MovePaneOptions { WindowId = "w1" });
        await _mux.SplitAsync(new SplitOptions(dash, SplitDirection.Right) { MovePane = browser });
        await _mux.SetTitleAsync(dash, "remove-pr-pipeline files");

        var result = await Handler.HandleAsync(Root, [Sub], AgentHarness.Orchestrator);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();
        var home = panes.Single(p => p.Id == harness);
        var mine = panes.Single(p => p.Id == dash);

        Assert.Equal(home.TabId, mine.TabId);
        Assert.Equal(FleetTabTitles.Dashboard, mine.Title);
        Assert.Equal(FleetTabTitles.Dashboard, home.Title);
        Assert.True(mine.IsActive);
        Assert.Contains(panes, p => p.Id == browser);
        Assert.NotEqual(mine.TabId, panes.Single(p => p.Id == browser).TabId);
        Assert.Contains("1 stray pane", result.Value);
    }

    [Fact]
    public async Task Without_a_harness_pane_a_fresh_one_is_started_on_the_left()
    {
        var (harness, dash) = await ProjectAsync();
        await _mux.KillPaneAsync(harness);

        var result = await Handler.HandleAsync(Root, [Sub], AgentHarness.Orchestrator);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();
        var mine = panes.Single(p => p.Id == dash);
        var fresh = Assert.Single(panes, p => p.Id != dash);

        Assert.Equal(mine.TabId, fresh.TabId);
        Assert.Equal(Root, fresh.Cwd);
        Assert.Equal(AgentHarness.CommandFor(AgentHarness.Orchestrator), _mux.ArgsFor(fresh.Id));
        Assert.Equal(FleetTabTitles.Dashboard, mine.Title);
    }

    [Fact]
    public async Task A_subs_claude_pane_at_the_project_root_is_never_mistaken_for_the_harness()
    {
        var (harness, dash) = await ProjectAsync();
        await _mux.KillPaneAsync(harness);
        var subClaude = await _mux.SpawnAsync(new SpawnOptions { Cwd = Root, Args = [AgentHarness.Claude] });
        await _mux.SetTitleAsync(subClaude, "remove-pr-pipeline");

        var result = await Handler.HandleAsync(Root, [Sub], AgentHarness.Orchestrator);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();
        Assert.NotEqual(panes.Single(p => p.Id == dash).TabId, panes.Single(p => p.Id == subClaude).TabId);
    }

    [Fact]
    public async Task Outside_a_known_pane_it_fails_instead_of_guessing()
    {
        await ProjectAsync();
        _mux.CurrentPane = new PaneId("p99");

        var result = await Handler.HandleAsync(Root, [], AgentHarness.Claude);

        Assert.False(result.Succeeded);
    }
}
