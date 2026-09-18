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
    public void A_visible_agent_is_restored_into_the_dashboards_window()
    {
        var options = RestoreSessionHandler.Options(
            "techweb", Agent("dev", open: true), "w7", native: false);

        Assert.Equal("w7", options.WindowId);
        Assert.Equal("techweb", options.SessionName);
        Assert.False(options.NewWindow);
        Assert.Null(options.Workspace);
        Assert.Equal(AgentHarness.CommandFor(AgentHarness.Nvim), options.Args);
    }

    [Fact]
    public void A_hidden_agent_is_restored_into_a_new_hidden_window_when_legacy()
    {
        var hidden = Agent("dev", open: true) with { Hidden = true };
        var options = RestoreSessionHandler.Options("techweb", hidden, "w7", native: false);

        Assert.Null(options.WindowId);
        Assert.True(options.NewWindow);
        Assert.Equal(FleetWorkspaces.Hidden, options.Workspace);
        Assert.Equal(FleetWorkspaces.Hidden, options.SessionName);
    }

    [Fact]
    public void A_native_project_restores_every_agent_into_its_own_window_hidden_or_not()
    {
        var visible = RestoreSessionHandler.Options(
            "techweb", Agent("dev", open: true), "w7", native: true);
        var hidden = RestoreSessionHandler.Options(
            "techweb", Agent("dev", open: true) with { Hidden = true }, "w7", native: true);

        foreach (var options in new[] { visible, hidden })
        {
            Assert.Equal("w7", options.WindowId);
            Assert.Equal("techweb", options.SessionName);
            Assert.False(options.NewWindow);
            Assert.Null(options.Workspace);
        }
    }
}
