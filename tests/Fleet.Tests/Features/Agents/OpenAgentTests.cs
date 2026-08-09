using Fleet.Features.Agents.OpenAgent;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Requests;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public sealed class OpenAgentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly RecordingWorkspaces _workspaces = new();

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
    public async Task A_running_agent_is_found_by_its_worktree_not_by_a_pane_id()
    {
        var agent = Agent();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = Path.Combine(_root, "backend", "main") });
        var wanted = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        var result = await new OpenAgentHandler(_mux, _workspaces).HandleAsync("techweb", agent);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();
        Assert.True(panes.Single(p => p.Id == wanted).IsActive);
    }

    [Fact]
    public async Task A_trailing_separator_still_matches_the_pane()
    {
        var agent = Agent();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree + Path.DirectorySeparatorChar });

        var result = await new OpenAgentHandler(_mux, _workspaces).HandleAsync("techweb", agent);

        Assert.True(result.Succeeded, result.Error);
        Assert.Single(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task An_agent_whose_pane_died_is_restarted_in_its_own_worktree()
    {
        var agent = Agent();

        var result = await new OpenAgentHandler(_mux, _workspaces).HandleAsync("techweb", agent);

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

        await new OpenAgentHandler(_mux, _workspaces).HandleAsync("techweb", agent);

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.Equal(AgentHarness.CommandFor(AgentHarness.Nvim), _mux.ArgsFor(pane.Id));
    }

    [Fact]
    public async Task A_worktree_that_no_longer_exists_is_reported_rather_than_respawned()
    {
        var agent = Agent();
        Directory.Delete(agent.Worktree, recursive: true);

        var result = await new OpenAgentHandler(_mux, _workspaces).HandleAsync("techweb", agent);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot be restarted", result.Error);
        Assert.Empty(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task Restarting_does_not_disturb_other_panes()
    {
        var agent = Agent();
        var other = await _mux.SpawnAsync(new SpawnOptions { Cwd = Path.Combine(_root, "other") });

        await new OpenAgentHandler(_mux, _workspaces).HandleAsync("techweb", agent);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(2, panes.Count);
        Assert.Contains(panes, p => p.Id == other);
    }

    private sealed class RecordingWorkspaces : IWorkspaceRequestStore
    {
        public List<string> Asked { get; } = [];

        public void Submit(string workspace) => Asked.Add(workspace);
    }
}
