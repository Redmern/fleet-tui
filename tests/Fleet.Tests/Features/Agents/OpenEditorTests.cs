using Fleet.Features.Agents;
using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.OpenEditor;
using Fleet.Features.Agents.StopAgent;
using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Projects.QuitProject;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Orchestrations;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Agents;

public sealed class OpenEditorTests : IDisposable
{
    private const string Project = "techweb";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    public OpenEditorTests() => Directory.CreateDirectory(ProjectRoot);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string ProjectRoot => Path.Combine(_root, "project");

    private AgentRecord Agent(string branch = "feature/login")
    {
        var worktree = Path.Combine(_root, "backend", branch.Replace('/', '_'));
        Directory.CreateDirectory(worktree);

        return new AgentRecord(worktree, "backend", branch, AgentHarness.Claude, "origin/main", true);
    }

    private AgentRecord Sub(string slug = "remove-pr-pipeline")
    {
        var folder = OrchestrationPaths.For(ProjectRoot, slug);
        Directory.CreateDirectory(folder);

        return new AgentRecord(folder, string.Empty, slug, AgentHarness.Orchestrator, string.Empty, false);
    }

    private OpenEditorHandler Handler => new(_mux);

    private Task<PaneId> DashboardAsync() =>
        _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot, SessionName = Project });

    private async Task<PaneId> AgentPaneAsync(AgentRecord agent)
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree, SessionName = Project });
        await _mux.SetTitleAsync(pane, AgentTitle.For(agent.Repository, agent.Branch));
        return pane;
    }

    private async Task<Pane> EditorOfAsync(AgentRecord agent) =>
        (await _mux.ListPanesAsync()).Single(p => AgentPaneMatch.IsEditor(p, agent));

    private int Opened => _mux.Calls.Count(c => c is "spawn" or "split");

    [Fact]
    public async Task A_repo_agent_gets_nvim_with_neo_tree_in_its_worktree()
    {
        var agent = Agent();
        await DashboardAsync();
        await AgentPaneAsync(agent);

        var result = await Handler.HandleAsync(Project, agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);
        var editor = await EditorOfAsync(agent);
        Assert.Equal(agent.Worktree, editor.Cwd);
        Assert.Equal(AgentHarness.BrowseCommandFor("backend/feature_login editor"), _mux.ArgsFor(editor.Id));
        Assert.True(editor.IsActive);
    }

    [Fact]
    public async Task A_sub_orchestrator_gets_nvim_in_its_orchestration_folder()
    {
        var sub = Sub();
        await DashboardAsync();
        await AgentPaneAsync(sub);

        var result = await Handler.HandleAsync(Project, sub, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);
        var editor = await EditorOfAsync(sub);
        Assert.Equal(Path.Combine(ProjectRoot, ".fleet", "orchestrations", "remove-pr-pipeline"), editor.Cwd);
        Assert.Equal("remove-pr-pipeline editor", editor.PaneTitle);
    }

    [Fact]
    public async Task An_open_agent_pane_gets_the_editor_split_beside_it_in_its_own_tab()
    {
        var agent = Agent();
        await DashboardAsync();
        var claude = await AgentPaneAsync(agent);

        await Handler.HandleAsync(Project, agent, ProjectRoot);

        var panes = await _mux.ListPanesAsync();
        Assert.Contains("split", _mux.Calls);
        Assert.Equal(panes.Single(p => p.Id == claude).TabId, (await EditorOfAsync(agent)).TabId);
        Assert.Equal("backend/feature_login", _mux.TitleOf(claude));
    }

    [Fact]
    public async Task An_editor_already_open_for_that_folder_is_focused_instead_of_opening_another()
    {
        var agent = Agent();
        var dashboard = await DashboardAsync();
        await AgentPaneAsync(agent);
        await Handler.HandleAsync(Project, agent, ProjectRoot);
        var first = await EditorOfAsync(agent);
        await _mux.FocusPaneAsync(dashboard);
        var before = Opened;

        var result = await Handler.HandleAsync(Project, agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(before, Opened);
        Assert.Single(await _mux.ListPanesAsync(), p => AgentPaneMatch.IsEditor(p, agent));
        Assert.True((await EditorOfAsync(agent)).IsActive);
        Assert.Equal(first.Id, (await EditorOfAsync(agent)).Id);
    }

    [Fact]
    public async Task An_agent_whose_pane_is_not_open_gets_the_editor_in_the_project_window()
    {
        var agent = Agent();
        var dashboard = await DashboardAsync();

        var result = await Handler.HandleAsync(Project, agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);
        var panes = await _mux.ListPanesAsync();
        var editor = await EditorOfAsync(agent);
        Assert.DoesNotContain("split", _mux.Calls);
        Assert.Equal(panes.Single(p => p.Id == dashboard).WindowId, editor.WindowId);
        Assert.Equal("backend/feature_login editor", _mux.TitleOf(editor.Id));
    }

    [Fact]
    public async Task A_hidden_agent_is_not_split_so_the_editor_opens_in_the_project_window()
    {
        var agent = Agent();
        var dashboard = await DashboardAsync();
        await _mux.SpawnAsync(new SpawnOptions
        {
            Cwd = agent.Worktree,
            Workspace = FleetWorkspaces.Hidden,
            NewWindow = true,
        });

        await Handler.HandleAsync(Project, agent, ProjectRoot);

        var panes = await _mux.ListPanesAsync();
        var editor = await EditorOfAsync(agent);
        Assert.DoesNotContain("split", _mux.Calls);
        Assert.Equal(panes.Single(p => p.Id == dashboard).WindowId, editor.WindowId);
        Assert.False(FleetWorkspaces.IsHidden(editor.SessionName));
    }

    [Fact]
    public async Task With_workspaces_a_hidden_agent_gets_the_editor_in_the_project_workspace()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        var agent = Agent();
        await mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot, SessionName = Project });
        await mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree, Workspace = FleetWorkspaces.HiddenFor(Project) });

        var result = await new OpenEditorHandler(mux).HandleAsync(Project, agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);
        var editor = (await mux.ListPanesAsync()).Single(p => AgentPaneMatch.IsEditor(p, agent));
        Assert.Equal(Project, editor.SessionName);
        Assert.DoesNotContain("split", mux.Calls);
    }

    [Fact]
    public async Task A_folder_that_is_gone_is_reported_rather_than_opened()
    {
        var agent = Agent();
        Directory.Delete(agent.Worktree);

        var result = await Handler.HandleAsync(Project, agent, ProjectRoot);

        Assert.False(result.Succeeded);
        Assert.Equal(0, Opened);
    }

    [Fact]
    public async Task An_editor_pane_is_never_the_agents_pane_even_sharing_its_folder_and_tab_title()
    {
        var agent = Agent();
        await DashboardAsync();
        var claude = await AgentPaneAsync(agent);
        await Handler.HandleAsync(Project, agent, ProjectRoot);

        var editor = await EditorOfAsync(agent);

        Assert.Equal(agent.Worktree, editor.Cwd);
        Assert.Equal("backend/feature_login", editor.Title);
        Assert.False(AgentPaneMatch.Owns(editor, agent));
        Assert.True(AgentPaneMatch.Owns((await _mux.ListPanesAsync()).Single(p => p.Id == claude), agent));
    }

    [Fact]
    public async Task An_agent_with_only_its_editor_open_is_restored_and_not_shown()
    {
        var agent = Agent() with { Open = true };
        await DashboardAsync();
        await Handler.HandleAsync(Project, agent, ProjectRoot);

        var panes = await _mux.ListPanesAsync();

        Assert.True(RestoreSessionHandler.Wanted(agent, panes));
        Assert.False(AgentPanes.Shown(agent, panes));
    }

    [Fact]
    public async Task Stopping_an_agent_leaves_its_editor_open()
    {
        var agent = Agent();
        await DashboardAsync();
        var claude = await AgentPaneAsync(agent);
        await Handler.HandleAsync(Project, agent, ProjectRoot);
        var editor = await EditorOfAsync(agent);
        var stopper = new StopAgentHandler(_mux, new RecordingStore());

        Assert.True((await stopper.HandleAsync(Project, agent)).Succeeded);

        var panes = await _mux.ListPanesAsync();
        Assert.DoesNotContain(panes, p => p.Id == claude);
        Assert.Contains(panes, p => p.Id == editor.Id);
        Assert.False((await stopper.HandleAsync(Project, agent)).Succeeded);
    }

    [Fact]
    public async Task Hiding_an_agent_moves_only_its_own_pane_not_the_editor()
    {
        var agent = Agent();
        await DashboardAsync();
        var claude = await AgentPaneAsync(agent);
        await Handler.HandleAsync(Project, agent, ProjectRoot);
        var editor = await EditorOfAsync(agent);

        await new HideAgentHandler(_mux, new RecordingStore()).HandleAsync(Project, agent, editor.WindowId);

        var panes = await _mux.ListPanesAsync();
        Assert.True(FleetWorkspaces.IsHidden(panes.Single(p => p.Id == claude).SessionName));
        Assert.Equal(editor.SessionName, panes.Single(p => p.Id == editor.Id).SessionName);
    }

    [Fact]
    public async Task Quitting_the_project_closes_agent_editors_too()
    {
        var agent = Agent();
        await DashboardAsync();
        await Handler.HandleAsync(Project, agent, ProjectRoot);
        var editor = await EditorOfAsync(agent);

        var doomed = QuitPlan.PanesToClose(await _mux.ListPanesAsync(), ProjectRoot, [agent]);

        Assert.Contains(editor.Id, doomed);
    }

    private static Pane Pane(string id, string window, string tab, string cwd, bool active, string paneTitle = "") =>
        new(new PaneId(id), window, tab, "techweb", "title", cwd, active, paneTitle);

    [Fact]
    public void From_a_floating_menu_the_agent_is_the_active_pane_underneath()
    {
        var one = Agent("one");
        var two = Agent("two");

        IReadOnlyList<Pane> panes =
        [
            Pane("1", "w", "t1", ProjectRoot, active: false),
            Pane("2", "w", "t2", two.Worktree, active: true),
            Pane("3", "w", "float", ProjectRoot, active: true),
        ];

        var caller = OpenEditorHandler.Caller(panes, new PaneId("3"), floating: true, one.Worktree, [one, two]);

        Assert.Equal(two, caller);
    }

    [Fact]
    public void From_a_menu_tab_the_agent_is_the_one_whose_folder_the_menu_inherited()
    {
        var one = Agent("one");
        var two = Agent("two");

        IReadOnlyList<Pane> panes =
        [
            Pane("1", "w", "t1", two.Worktree, active: true),
            Pane("2", "w", "t2", one.Worktree, active: true),
        ];

        var caller = OpenEditorHandler.Caller(panes, new PaneId("2"), floating: false, one.Worktree, [one, two]);

        Assert.Equal(one, caller);
    }

    [Fact]
    public void From_an_editor_pane_the_menu_acts_on_the_agent_it_belongs_to()
    {
        var agent = Agent();
        var editor = Pane("1", "w", "t1", agent.Worktree, active: true, AgentPaneMatch.EditorTitle(agent));

        var caller = OpenEditorHandler.Caller(
            [editor, Pane("2", "w", "float", ProjectRoot, active: true)],
            new PaneId("2"),
            floating: true,
            ProjectRoot,
            [agent]);

        Assert.Equal(agent, caller);
    }

    [Fact]
    public void A_pane_that_is_no_agent_has_no_caller()
    {
        var agent = Agent();

        Assert.Null(OpenEditorHandler.Caller([], PaneId.None, floating: false, ProjectRoot, [agent]));
    }

    [Fact]
    public async Task The_editor_can_open_from_an_agent_pane()
    {
        var agent = Agent();

        Assert.True(await OpenEditorHandler.CanOpenEditorAsync(_mux, agent.Worktree, [agent]));
    }

    [Fact]
    public async Task The_editor_cannot_open_from_a_pane_that_is_no_agent()
    {
        var agent = Agent();

        Assert.False(await OpenEditorHandler.CanOpenEditorAsync(_mux, ProjectRoot, [agent]));
    }

    [Fact]
    public async Task The_editor_cannot_open_without_a_usable_multiplexer()
    {
        var agent = Agent();

        Assert.False(await OpenEditorHandler.CanOpenEditorAsync(null, agent.Worktree, [agent]));
    }

    [Fact]
    public void The_default_key_is_e_and_only_the_settings_submenu_shares_it()
    {
        Assert.Equal("e", KeymapDefaults.Bindings[FleetAction.OpenEditor]);
        Assert.Equal(
            [FleetAction.EditFleetConfig, FleetAction.OpenEditor],
            KeymapDefaults.Bindings.Where(b => b.Value == "e").Select(b => b.Key).Order());
        Assert.Equal(FleetAction.OpenEditor, FleetActionIds.Parse(FleetActionIds.For(FleetAction.OpenEditor)));
    }

    [Theory]
    [InlineData(DashboardTabs.AgentsTab)]
    [InlineData(DashboardTabs.SubsTab)]
    public void E_opens_the_editor_on_the_agent_and_sub_tabs(int tab)
    {
        Assert.Equal(FleetAction.OpenEditor, DashboardKeys.For(Key.E, Keymap.Default, tab).Action);
    }

    [Fact]
    public void E_does_nothing_on_the_repositories_tab()
    {
        Assert.False(DashboardKeys.For(Key.E, Keymap.Default, DashboardTabs.RepositoriesTab).Consume);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public void Save(string project, AgentRecord agent) { }

        public IReadOnlyList<AgentRecord> List(string project) => [];

        public void Remove(string project, string worktree) { }
    }
}
