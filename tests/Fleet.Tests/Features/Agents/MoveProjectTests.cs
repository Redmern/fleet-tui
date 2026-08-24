using Fleet.Features.Agents.MoveProject;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public class MoveProjectTests
{
    private const string Root = "C:/repos/techweb";

    private readonly FakeMuxDriver _mux = new();

    private static AgentRecord Agent(string branch = "feature/login", bool hidden = false) =>
        new($"{Root}/backend/{branch.Replace('/', '_')}", "backend", branch, "nvim",
            "origin/main", true, hidden, Open: true);

    private static AgentRecord Sub(string slug = "upgrade") =>
        new($"{Root}/.fleet/orchestrations/{slug}", string.Empty, slug,
            AgentHarness.Orchestrator, string.Empty, false, Hidden: false, Open: true);

    private async Task<(PaneId Claude, PaneId Dash)> ProjectAsync()
    {
        var claude = await _mux.SpawnAsync(new SpawnOptions { Cwd = Root, NewWindow = true });
        var dash = await _mux.SpawnAsync(new SpawnOptions { Cwd = Root });

        return (claude, dash);
    }

    private MoveProjectHandler Handler() => new(_mux);

    [Fact]
    public async Task Moves_the_claude_pane_intact_and_respawns_the_dash_beside_it()
    {
        var (claude, dash) = await ProjectAsync();
        var other = await _mux.SpawnAsync(new SpawnOptions { Cwd = "C:/elsewhere", NewWindow = true });
        var dest = (await _mux.ListPanesAsync()).Single(p => p.Id == other).WindowId;

        var moved = await Handler().HandleAsync(
            "techweb", Root, [], dest, dash.Value, null, "fleet");

        Assert.True(moved.Succeeded, moved.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.DoesNotContain(panes, p => p.Id == dash);
        Assert.Equal(dest, panes.Single(p => p.Id == claude).WindowId);
        Assert.Equal(FleetTabTitles.Dashboard, _mux.TitleOf(claude));

        var newDash = panes.Single(p =>
            p.Id != claude && _mux.ArgsFor(p.Id).Contains("dash"));

        Assert.Equal(["fleet", "dash", "--project", "techweb"], _mux.ArgsFor(newDash.Id));
        Assert.True(panes.Single(p => p.Id == newDash.Id).IsActive);
    }

    [Fact]
    public async Task Moving_to_a_new_window_lands_claude_and_agents_in_one_window()
    {
        var (claude, dash) = await ProjectAsync();
        var agent = Agent();
        var agentPane = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        await _mux.SetTitleAsync(agentPane, "backend/feature_login");

        var moved = await Handler().HandleAsync(
            "techweb", Root, [agent], null, dash.Value, null, "fleet");

        Assert.True(moved.Succeeded, moved.Error);

        var panes = await _mux.ListPanesAsync();
        var landed = panes.Single(p => p.Id == claude).WindowId;

        Assert.Equal(landed, panes.Single(p => p.Id == agentPane).WindowId);
    }

    [Fact]
    public async Task A_subs_browser_is_killed_and_rebuilt_beside_its_moved_claude()
    {
        var (_, dash) = await ProjectAsync();
        var sub = Sub();
        var subClaude = await _mux.SpawnAsync(new SpawnOptions { Cwd = sub.Worktree });
        await _mux.SetTitleAsync(subClaude, sub.Branch);
        var browser = await _mux.SpawnAsync(new SpawnOptions { Cwd = sub.Worktree });
        await _mux.SetTitleAsync(browser, $"{sub.Branch} files");

        var moved = await Handler().HandleAsync(
            "techweb", Root, [sub], null, dash.Value, null, "fleet");

        Assert.True(moved.Succeeded, moved.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.DoesNotContain(panes, p => p.Id == browser);
        Assert.Contains(panes, p =>
            p.Id != browser
            && _mux.ArgsFor(p.Id).SequenceEqual(AgentHarness.BrowseCommand));
    }

    [Fact]
    public async Task Hidden_agents_stay_in_the_hidden_workspace()
    {
        var (_, dash) = await ProjectAsync();
        var agent = Agent(hidden: true);
        var hiddenPane = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = agent.Worktree, Workspace = FleetWorkspaces.Hidden });

        var moved = await Handler().HandleAsync(
            "techweb", Root, [agent], null, dash.Value, null, "fleet");

        Assert.True(moved.Succeeded, moved.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(
            FleetWorkspaces.Hidden, panes.Single(p => p.Id == hiddenPane).SessionName);
    }

    [Fact]
    public async Task The_menu_pane_itself_is_never_killed_even_at_the_project_root()
    {
        var (_, dash) = await ProjectAsync();
        var menu = await _mux.SpawnAsync(new SpawnOptions { Cwd = Root });

        var moved = await Handler().HandleAsync(
            "techweb", Root, [], null, dash.Value, menu.Value, "fleet");

        Assert.True(moved.Succeeded, moved.Error);
        Assert.Contains(await _mux.ListPanesAsync(), p => p.Id == menu);
    }

    [Fact]
    public async Task An_unknown_dash_pane_falls_back_to_killing_and_resuming_claude()
    {
        var (claude, dash) = await ProjectAsync();

        var moved = await Handler().HandleAsync(
            "techweb", Root, [], null, "no-such-pane", null, "fleet");

        Assert.True(moved.Succeeded, moved.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.DoesNotContain(panes, p => p.Id == claude);
        Assert.DoesNotContain(panes, p => p.Id == dash);

        var resumed = panes.Single(p =>
            _mux.ArgsFor(p.Id).SequenceEqual(
                new[] { AgentHarness.Claude, AgentHarness.ResumeArgument }));

        Assert.Equal(FleetTabTitles.Dashboard, _mux.TitleOf(resumed.Id));
    }

    [Fact]
    public async Task A_project_with_no_panes_is_reopened_with_a_resumed_claude()
    {
        var moved = await Handler().HandleAsync(
            "techweb", Root, [], null, null, null, "fleet");

        Assert.True(moved.Succeeded, moved.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.Contains(panes, p => _mux.ArgsFor(p.Id)
            .SequenceEqual(new[] { AgentHarness.Claude, AgentHarness.ResumeArgument }));
    }

    [Fact]
    public async Task Parking_kills_the_dash_and_moves_claude_and_agents_to_hidden()
    {
        var (claude, dash) = await ProjectAsync();
        var agent = Agent();
        var agentPane = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        await _mux.SetTitleAsync(agentPane, "backend/feature_login");

        var parked = await Handler().ParkAsync(
            "techweb", Root, [agent], dash.Value, null);

        Assert.True(parked.Succeeded, parked.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.DoesNotContain(panes, p => p.Id == dash);
        Assert.Equal(FleetWorkspaces.Hidden, panes.Single(p => p.Id == claude).SessionName);
        Assert.Equal(FleetWorkspaces.Hidden, panes.Single(p => p.Id == agentPane).SessionName);
    }

    [Fact]
    public async Task A_parked_project_is_shown_again_with_its_claude_intact()
    {
        var (claude, dash) = await ProjectAsync();
        var agent = Agent();
        var agentPane = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        await _mux.SetTitleAsync(agentPane, "backend/feature_login");

        Assert.True((await Handler().ParkAsync(
            "techweb", Root, [agent], dash.Value, null)).Succeeded);

        var other = await _mux.SpawnAsync(new SpawnOptions { Cwd = "C:/x", NewWindow = true });
        var dest = (await _mux.ListPanesAsync()).Single(p => p.Id == other).WindowId;

        var shown = await Handler().HandleAsync(
            "techweb", Root, [agent], dest, null, null, "fleet");

        Assert.True(shown.Succeeded, shown.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(dest, panes.Single(p => p.Id == claude).WindowId);
        Assert.Equal(dest, panes.Single(p => p.Id == agentPane).WindowId);
        Assert.Contains(panes, p => _mux.ArgsFor(p.Id).Contains("dash"));
    }
}
