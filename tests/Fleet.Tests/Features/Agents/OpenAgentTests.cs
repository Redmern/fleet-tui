using Fleet.Features.Agents;
using Fleet.Features.Agents.OpenAgent;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Agents;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public sealed class OpenAgentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly RecordingStore _store = new();

    private string ProjectRoot => Path.Combine(_root, "project");

    public OpenAgentTests() => Directory.CreateDirectory(_root);

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

    private AgentRecord Agent(string branch = "feature/login")
    {
        var worktree = Path.Combine(_root, "backend", branch.Replace('/', '_'));
        Directory.CreateDirectory(worktree);

        return new AgentRecord(worktree, "backend", branch, "claude", "origin/main", true);
    }

    [Fact]
    public async Task A_pane_living_in_another_window_is_brought_here_before_it_is_focused()
    {
        var agent = Agent();

        Directory.CreateDirectory(ProjectRoot);

        var dashboard = await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });

        var stray = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = agent.Worktree, NewWindow = true });

        var panes = await _mux.ListPanesAsync();
        var home = panes.Single(p => p.Id == dashboard).WindowId;

        Assert.NotEqual(home, panes.Single(p => p.Id == stray).WindowId);

        var result = await new OpenAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var after = await _mux.ListPanesAsync();

        Assert.Equal(home, after.Single(p => p.Id == stray).WindowId);
        Assert.True(after.Single(p => p.Id == stray).IsActive);
        Assert.Equal("backend/feature_login", _mux.TitleOf(stray));
    }

    [Fact]
    public async Task A_running_agent_is_found_by_its_worktree_not_by_a_pane_id()
    {
        var agent = Agent();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = Path.Combine(_root, "backend", "main") });
        var wanted = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        var result = await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();
        Assert.True(panes.Single(p => p.Id == wanted).IsActive);
    }

    [Fact]
    public async Task A_trailing_separator_still_matches_the_pane()
    {
        var agent = Agent();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree + Path.DirectorySeparatorChar });

        var result = await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);
        Assert.Single(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task An_agent_whose_pane_died_is_restarted_in_its_own_worktree()
    {
        var agent = Agent();

        var result = await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.Equal(agent.Worktree, pane.Cwd);
        Assert.Equal([AgentHarness.Claude], _mux.ArgsFor(pane.Id));
        Assert.Equal("backend/feature_login", _mux.TitleOf(pane.Id));
    }

    [Fact]
    public async Task A_restarted_agent_keeps_the_harness_it_was_created_with()
    {
        var agent = Agent() with { Harness = AgentHarness.Nvim };

        await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.Equal(AgentHarness.CommandFor(AgentHarness.Nvim), _mux.ArgsFor(pane.Id));
    }

    [Fact]
    public async Task A_worktree_that_no_longer_exists_is_reported_rather_than_respawned()
    {
        var agent = Agent();
        Directory.Delete(agent.Worktree, recursive: true);

        var result = await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot be restarted", result.Error);
        Assert.Empty(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task Restarting_does_not_disturb_other_panes()
    {
        var agent = Agent();
        var other = await _mux.SpawnAsync(new SpawnOptions { Cwd = Path.Combine(_root, "other") });

        await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(2, panes.Count);
        Assert.Contains(panes, p => p.Id == other);
    }

    [Fact]
    public async Task Opening_an_orchestrator_splits_a_file_browser_alongside_it()
    {
        var agent = Agent() with { Harness = AgentHarness.Orchestrator };

        var result = await new OpenAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(2, panes.Count);

        var browser = panes.Single(p => SubBrowse.Is(p));
        var claude = panes.Single(p => !SubBrowse.Is(p));

        Assert.Equal(
            AgentHarness.BrowseCommandFor("backend/feature_login files"), _mux.ArgsFor(browser.Id));
        Assert.Equal("backend/feature_login files", browser.PaneTitle);
        Assert.Equal(claude.TabId, browser.TabId);
        Assert.Equal("backend/feature_login", _mux.TitleOf(claude.Id));
        Assert.Equal("backend/feature_login", _mux.TitleOf(browser.Id));
    }

    [Fact]
    public async Task Opening_a_sub_that_is_already_open_leaves_both_of_its_panes_alone()
    {
        var agent = Agent() with { Harness = AgentHarness.Orchestrator };
        var claude = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        await _mux.SetTitleAsync(claude, AgentTitle.For(agent.Repository, agent.Branch));
        await SubBrowse.SplitAsync(_mux, agent, claude);
        var browser = (await _mux.ListPanesAsync()).Single(p => p.Id != claude).Id;

        var result = await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(2, panes.Count);
        Assert.Contains(panes, p => p.Id == claude);
        Assert.Contains(panes, p => p.Id == browser);
        Assert.True(panes.Single(p => p.Id == claude).IsActive);
    }

    [Fact]
    public async Task A_plain_agent_opens_a_single_pane_with_no_browser()
    {
        var agent = Agent();

        await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        Assert.Single(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task Opening_a_hidden_orchestrator_brings_claude_back_rebuilds_the_split_and_focuses_it()
    {
        Directory.CreateDirectory(ProjectRoot);
        var dash = await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });
        var window = (await _mux.ListPanesAsync()).Single(p => p.Id == dash).WindowId;

        var agent = Agent() with { Harness = AgentHarness.Orchestrator, Hidden = true };

        // A hidden sub is only its claude pane in the hidden workspace; the browser was dropped on hide.
        var claude = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = agent.Worktree, Workspace = "fleet-hidden", NewWindow = true });

        var result = await new OpenAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(window, panes.Single(p => p.Id == claude).WindowId);
        Assert.True(panes.Single(p => p.Id == claude).IsActive);
        Assert.Contains(panes, p => PathKey.Same(p.Cwd, agent.Worktree) && SubBrowse.Is(p));
        Assert.False(Assert.Single(_store.Saved).Hidden);
    }

    [Fact]
    public async Task Opening_an_orchestrator_whose_claude_died_rebuilds_the_split()
    {
        var agent = Agent() with { Harness = AgentHarness.Orchestrator };

        // Only the browser survives — the claude pane exited.
        var browser = await _mux.SpawnAsync(
            new SpawnOptions
            {
                Cwd = agent.Worktree,
                Args = AgentHarness.BrowseCommandFor(SubBrowse.Title(agent)),
            });
        await _mux.SetTitleAsync(browser, AgentTitle.For(agent.Repository, agent.Branch));

        var result = await new OpenAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();

        // The stale lone pane is gone; a fresh claude + browser split stands in its place.
        Assert.DoesNotContain(panes, p => p.Id == browser);
        var atWorktree = panes.Where(p => PathKey.Same(p.Cwd, agent.Worktree)).ToList();
        Assert.Equal(2, atWorktree.Count);
        Assert.Contains(atWorktree, p => SubBrowse.Is(p));
        Assert.Single(atWorktree, p =>
            _mux.ArgsFor(p.Id).SequenceEqual(AgentHarness.OrchestratorCommand(resume: true)));
    }

    [Fact]
    public async Task Opening_an_orchestrator_never_spawns_an_extra_pane_when_it_already_has_them()
    {
        var agent = Agent() with { Harness = AgentHarness.Orchestrator };
        var claude = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        await _mux.SetTitleAsync(claude, AgentTitle.For(agent.Repository, agent.Branch));
        await SubBrowse.SplitAsync(_mux, agent, claude);

        await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        var atWorktree = (await _mux.ListPanesAsync())
            .Count(p => PathKey.Same(p.Cwd, agent.Worktree));

        Assert.Equal(2, atWorktree);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<AgentRecord> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Saved;

        public void Remove(string project, string worktree) { }
    }

    [Fact]
    public async Task Opening_a_hidden_agent_unhides_it_rather_than_leaving_it_aside()
    {
        var agent = Agent() with { Hidden = true };
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree, Workspace = "fleet-hidden" });

        var result = await new OpenAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);
        Assert.False(Assert.Single(_store.Saved).Hidden);
    }

    [Fact]
    public async Task A_hidden_agent_with_no_pane_restarts_visible_not_hidden()
    {
        var agent = Agent() with { Hidden = true };

        var result = await new OpenAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.NotEqual("fleet-hidden", pane.SessionName);
        Assert.False(Assert.Single(_store.Saved).Hidden);
    }

    [Fact]
    public async Task Opening_an_agent_records_that_it_is_open_so_a_crash_can_be_recovered()
    {
        var agent = Agent();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        Assert.True(Assert.Single(_store.Saved).Open);
    }

    [Fact]
    public async Task An_agent_already_recorded_as_open_is_not_written_again()
    {
        var agent = Agent() with { Open = true };
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", agent, ProjectRoot);

        Assert.Empty(_store.Saved);
    }
}
