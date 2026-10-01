using Fleet.Features.Projects.RestoreSession;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Projects;

public sealed class RestoreSessionTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    public RestoreSessionTests() => Directory.CreateDirectory(_root);

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

    private string ProjectRoot => Path.Combine(_root, "techweb");

    private AgentRecord Agent(string branch, bool open, bool hidden = false)
    {
        var worktree = Path.Combine(ProjectRoot, "backend", branch);
        Directory.CreateDirectory(worktree);

        return new AgentRecord(
            worktree, "backend", branch, AgentHarness.Nvim, "origin/develop", true, hidden, open);
    }

    [Fact]
    public async Task Agents_that_were_open_come_back_and_the_others_stay_shut()
    {
        Directory.CreateDirectory(ProjectRoot);

        await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });

        var restored = await new RestoreSessionHandler(_mux).HandleAsync(
            "techweb",
            ProjectRoot,
            [Agent("dev", open: true), Agent("spike", open: false)]);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(1, restored);
        Assert.Contains(panes, p => p.Cwd.EndsWith("dev", StringComparison.Ordinal));
        Assert.DoesNotContain(panes, p => p.Cwd.EndsWith("spike", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_orchestrator_started_agent_comes_back_with_claude_and_a_manual_one_without()
    {
        Directory.CreateDirectory(ProjectRoot);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });

        await new RestoreSessionHandler(_mux).HandleAsync(
            "techweb", ProjectRoot, [Agent("health", open: true) with { Claude = true }, Agent("notes", open: true)]);

        var panes = await _mux.ListPanesAsync();
        var health = panes.Single(p => p.Cwd.EndsWith("health", StringComparison.Ordinal));
        var notes = panes.Single(p => p.Cwd.EndsWith("notes", StringComparison.Ordinal));
        Assert.Contains(_mux.ArgsFor(health.Id), arg => arg.Contains("ClaudeCode", StringComparison.Ordinal));
        Assert.DoesNotContain(_mux.ArgsFor(notes.Id), arg => arg.Contains("ClaudeCode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hidden_agent_comes_back_hidden()
    {
        Directory.CreateDirectory(ProjectRoot);

        await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });

        await new RestoreSessionHandler(_mux).HandleAsync(
            "techweb", ProjectRoot, [Agent("dev", open: true, hidden: true)]);

        var panes = await _mux.ListPanesAsync();

        var restored = panes.Single(p => p.Cwd.EndsWith("dev", StringComparison.Ordinal));

        Assert.Equal(FleetWorkspaces.Hidden, restored.SessionName);
    }

    [Fact]
    public async Task An_agent_that_is_already_running_is_left_alone()
    {
        var agent = Agent("dev", open: true);

        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        var restored = await new RestoreSessionHandler(_mux)
            .HandleAsync("techweb", ProjectRoot, [agent]);

        Assert.Equal(0, restored);
        Assert.Single(await _mux.ListPanesAsync());
    }

    [Fact]
    public void A_worktree_that_is_gone_is_not_restored()
    {
        var missing = new AgentRecord(
            Path.Combine(_root, "vanished"), "backend", "dev", AgentHarness.Nvim,
            "origin/develop", true, false, true);

        Assert.False(RestoreSessionHandler.Wanted(missing, []));
    }

    [Fact]
    public void A_hidden_agent_is_restored_into_its_own_projects_hidden_workspace_when_there_are_workspaces()
    {
        var hidden = Agent("dev", open: true, hidden: true);

        Assert.Equal(FleetWorkspaces.HiddenFor("techweb"), RestoreSessionHandler.Options("techweb", hidden, null, workspaces: true).Workspace);
        Assert.Equal(FleetWorkspaces.Hidden, RestoreSessionHandler.Options("techweb", hidden, null).Workspace);
    }

    [Fact]
    public async Task With_workspaces_agents_are_restored_into_their_project_even_when_another_project_shares_its_folder()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        Directory.CreateDirectory(ProjectRoot);
        await mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot, SessionName = "other" });
        await mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot, SessionName = "techweb" });

        await new RestoreSessionHandler(mux).HandleAsync("techweb", ProjectRoot, [Agent("dev", open: true)]);

        var dev = (await mux.ListPanesAsync()).Single(p => p.Cwd.EndsWith("dev", StringComparison.Ordinal));
        Assert.Equal("techweb", dev.SessionName);
    }

    [Fact]
    public void A_visible_agent_is_restored_into_the_dashboards_window()
    {
        var options = RestoreSessionHandler.Options("techweb", Agent("dev", open: true), "w7");

        Assert.Equal("w7", options.WindowId);
        Assert.Equal("techweb", options.SessionName);
        Assert.False(options.NewWindow);
        Assert.Null(options.Workspace);
        Assert.Equal(AgentHarness.CommandFor(AgentHarness.Nvim), options.Args);
    }

    [Fact]
    public void A_sub_orchestrator_is_restored_resuming_its_claude_inside_nvim()
    {
        var worktree = Path.Combine(ProjectRoot, "orchestrations", "sub");
        Directory.CreateDirectory(worktree);
        var sub = new AgentRecord(
            worktree, "orchestrations", "sub", AgentHarness.Orchestrator, string.Empty, false, false, true);

        var options = RestoreSessionHandler.Options("techweb", sub, "w7");

        Assert.Equal(AgentHarness.OrchestratorCommand(resume: true), options.Args);
    }
}
