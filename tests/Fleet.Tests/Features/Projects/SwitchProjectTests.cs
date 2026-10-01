using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Projects.LocateProject;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.QuitProject;
using Fleet.Features.Projects.SwitchProject;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Projects;

public class SwitchProjectTests
{
    private static readonly Project Techweb = new("techweb", "C:/repos/techweb");
    private static readonly Project Fleet = new("fleet", "C:/repos/fleet");

    private readonly FakeMuxDriver _mux = new(workspaces: true);

    private async Task<OpenProjectResult> OpenAsync(Project project)
    {
        var opened = await new OpenProjectHandler(_mux)
            .HandleAsync(new OpenProjectCommand(project, "claude", "fleet", null));

        Assert.True(opened.Succeeded, opened.Error);
        return opened.Value!;
    }

    private async Task<IReadOnlyList<(PaneId Id, string Workspace)>> SnapshotAsync() =>
        (await _mux.ListPanesAsync()).Select(p => (p.Id, p.SessionName)).ToList();

    [Fact]
    public async Task Switching_only_shows_a_workspace_and_never_kills_or_spawns()
    {
        await OpenAsync(Techweb);
        await OpenAsync(Fleet);
        await _mux.ShowWorkspaceAsync(Techweb.Name);
        var before = _mux.Calls.Count;

        var switched = await new SwitchProjectHandler(_mux).HandleAsync(Fleet.Name);

        Assert.True(switched.Succeeded, switched.Error);
        Assert.Equal(["show"], _mux.Calls.Skip(before));
        Assert.Equal(Fleet.Name, _mux.ShowingFor(_mux.CurrentClient));
    }

    [Fact]
    public async Task Panes_are_the_same_panes_after_a_hide_show_round_trip()
    {
        await OpenAsync(Techweb);
        await OpenAsync(Fleet);
        var switcher = new SwitchProjectHandler(_mux);
        var before = await SnapshotAsync();

        await switcher.HandleAsync(Techweb.Name);
        await switcher.HandleAsync(Fleet.Name);
        await switcher.HandleAsync(Techweb.Name);

        Assert.Equal(before, await SnapshotAsync());
        Assert.DoesNotContain("kill", _mux.Calls);
        Assert.DoesNotContain("move", _mux.Calls);
    }

    [Fact]
    public async Task Two_clients_show_different_projects_and_a_switch_by_one_leaves_the_other_alone()
    {
        await OpenAsync(Techweb);
        await OpenAsync(Fleet);
        var switcher = new SwitchProjectHandler(_mux);

        _mux.CurrentClient = "laptop";
        await switcher.HandleAsync(Techweb.Name);
        _mux.CurrentClient = "desktop";
        await switcher.HandleAsync(Fleet.Name);

        _mux.CurrentClient = "laptop";
        await switcher.HandleAsync(Fleet.Name);
        await switcher.HandleAsync(Techweb.Name);

        Assert.Equal(Techweb.Name, _mux.ShowingFor("laptop"));
        Assert.Equal(Fleet.Name, _mux.ShowingFor("desktop"));
    }

    [Fact]
    public async Task Switching_to_a_project_that_is_not_open_fails_without_creating_anything()
    {
        await OpenAsync(Techweb);
        var before = await SnapshotAsync();

        var switched = await new SwitchProjectHandler(_mux).HandleAsync(Fleet.Name);

        Assert.False(switched.Succeeded);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task A_hidden_project_still_counts_as_open_but_not_as_shown_here()
    {
        await OpenAsync(Techweb);
        await OpenAsync(Fleet);
        await new SwitchProjectHandler(_mux).HandleAsync(Fleet.Name);

        var located = await new LocateProjectHandler(_mux).HandleAsync([Techweb, Fleet]);

        Assert.True(located[Techweb.Name].Open);
        Assert.False(located[Techweb.Name].ShownHere);
        Assert.True(located[Fleet.Name].Open);
        Assert.True(located[Fleet.Name].ShownHere);
    }

    [Fact]
    public async Task What_one_client_shows_is_not_shown_here_for_another()
    {
        await OpenAsync(Techweb);
        _mux.CurrentClient = "laptop";
        await new SwitchProjectHandler(_mux).HandleAsync(Techweb.Name);

        _mux.CurrentClient = "desktop";
        var located = await new LocateProjectHandler(_mux).HandleAsync([Techweb]);

        Assert.True(located[Techweb.Name].Open);
        Assert.False(located[Techweb.Name].ShownHere);
    }

    [Fact]
    public async Task Without_workspaces_a_project_is_located_by_its_panes_and_the_current_window()
    {
        var mux = new FakeMuxDriver();
        var here = await mux.SpawnAsync(new SpawnOptions { Cwd = Techweb.Root, NewWindow = true });
        await mux.SpawnAsync(new SpawnOptions { Cwd = Fleet.Root, NewWindow = true });
        mux.CurrentPane = here;

        var located = await new LocateProjectHandler(mux).HandleAsync([Techweb, Fleet]);

        Assert.True(located[Techweb.Name].ShownHere);
        Assert.True(located[Fleet.Name].Open);
        Assert.False(located[Fleet.Name].ShownHere);
        Assert.False(SwitchProjectHandler.Applies(mux));
    }

    [Fact]
    public async Task Hiding_an_agent_moves_it_into_its_projects_hidden_workspace_without_killing_it()
    {
        await OpenAsync(Techweb);
        var agent = new AgentRecord(
            "C:/repos/techweb/backend/test", "backend", "test", AgentHarness.Claude,
            "origin/main", true);
        var pane = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = agent.Worktree, WindowId = Techweb.Name });

        var hidden = await new HideAgentHandler(_mux, new MemoryStore())
            .HandleAsync(Techweb.Name, agent, dashboardWindow: Techweb.Name);

        Assert.True(hidden.Succeeded, hidden.Error);
        var moved = (await _mux.ListPanesAsync()).Single(p => p.Id == pane);
        Assert.Equal(FleetWorkspaces.HiddenFor(Techweb.Name), moved.SessionName);
        Assert.DoesNotContain("kill", _mux.Calls);
    }

    [Fact]
    public async Task Hiding_an_orchestrator_keeps_its_browser_running_beside_it()
    {
        await OpenAsync(Techweb);
        var sub = new AgentRecord(
            "C:/repos/techweb/.fleet/orchestrations/upgrade", string.Empty, "upgrade",
            AgentHarness.Orchestrator, string.Empty, false);
        var main = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = sub.Worktree, WindowId = Techweb.Name });
        var browser = await _mux.SplitAsync(
            new SplitOptions(main, SplitDirection.Right) { Cwd = sub.Worktree });
        _mux.SetPaneTitle(browser, "upgrade files");

        await new HideAgentHandler(_mux, new MemoryStore())
            .HandleAsync(Techweb.Name, sub, dashboardWindow: Techweb.Name);

        var panes = await _mux.ListPanesAsync();
        var hiddenMain = panes.Single(p => p.Id == main);
        var hiddenBrowser = panes.Single(p => p.Id == browser);
        Assert.Equal(FleetWorkspaces.HiddenFor(Techweb.Name), hiddenMain.SessionName);
        Assert.Equal(hiddenMain.TabId, hiddenBrowser.TabId);
        Assert.DoesNotContain("kill", _mux.Calls);
    }

    [Fact]
    public async Task Quitting_a_project_closes_its_workspace_and_hidden_workspace_and_nothing_else()
    {
        await OpenAsync(Techweb);
        await OpenAsync(Fleet);
        var agent = new AgentRecord(
            "C:/repos/techweb/backend/test", "backend", "test", AgentHarness.Claude,
            "origin/main", true, Hidden: true, Open: true);
        await _mux.SpawnAsync(new SpawnOptions
        {
            Cwd = agent.Worktree,
            Workspace = FleetWorkspaces.HiddenFor(Techweb.Name),
        });

        await new QuitProjectHandler(_mux, new MemoryStore())
            .HandleAsync(Techweb.Name, Techweb.Root, [agent]);

        var left = (await _mux.ListWorkspacesAsync()).Select(w => w.Name).ToList();
        Assert.Equal([Fleet.Name], left);
    }

    private sealed class MemoryStore : IAgentStore
    {
        private readonly List<AgentRecord> _saved = [];

        public void Save(string project, AgentRecord agent) => _saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => _saved;

        public void Remove(string project, string worktree) { }
    }
}
