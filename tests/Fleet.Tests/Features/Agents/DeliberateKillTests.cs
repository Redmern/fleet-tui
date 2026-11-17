using Fleet.Features.Agents;
using Fleet.Features.Agents.OpenAgent;
using Fleet.Features.Projects.QuitProject;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared;
using Fleet.Shared.Status;

namespace Fleet.Tests.Features.Agents;

public sealed class DeliberateKillTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly MemoryStore _store = new();

    public DeliberateKillTests() => Directory.CreateDirectory(_root);

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

    private AgentRecord Agent(string branch, string harness = AgentHarness.Claude, string orchestratorHarness = "")
    {
        var worktree = Path.Combine(_root, "backend", branch);
        Directory.CreateDirectory(worktree);

        return new AgentRecord(
            worktree, "backend", branch, harness, "origin/main", true, Open: true, OrchestratorHarness: orchestratorHarness);
    }

    [Fact]
    public async Task Quitting_the_project_marks_every_agent_with_a_pane_as_closing_and_no_other()
    {
        var live = Agent("live");
        var idle = Agent("idle");
        await _mux.SpawnAsync(new SpawnOptions { Cwd = _root });
        await _mux.SpawnAsync(new SpawnOptions { Cwd = live.Worktree });

        var result = await new QuitProjectHandler(_mux, _store).HandleAsync("techweb", _root, [live, idle]);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(ClosingMark.Exists(live.Worktree));
        Assert.False(ClosingMark.Exists(idle.Worktree));
    }

    [Fact]
    public async Task Replacing_a_stale_orchestrator_pane_marks_it_closing_and_a_claude_one_keeps_the_mark_until_its_session_starts()
    {
        var sub = Agent("lead", AgentHarness.Orchestrator);
        await SpawnStaleBrowserAsync(sub);

        var result = await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", sub, _root);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(ClosingMark.Exists(sub.Worktree));
    }

    [Fact]
    public async Task Replacing_a_stale_pane_of_a_harness_without_hooks_clears_the_mark_once_it_opens_again()
    {
        var sub = Agent("lead", AgentHarness.Orchestrator, orchestratorHarness: AgentHarness.Opencode);
        await SpawnStaleBrowserAsync(sub);

        var result = await new OpenAgentHandler(_mux, _store).HandleAsync("techweb", sub, _root);

        Assert.True(result.Succeeded, result.Error);
        Assert.False(ClosingMark.Exists(sub.Worktree));
    }

    [Fact]
    public async Task Restoring_a_session_clears_the_mark_of_a_harness_without_hooks_and_keeps_a_claude_one()
    {
        var opencode = Agent("op", AgentHarness.Opencode);
        var claude = Agent("cl");
        ClosingMark.Write(opencode.Worktree);
        ClosingMark.Write(claude.Worktree);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = _root });

        await new RestoreSessionHandler(_mux).HandleAsync("techweb", _root, [opencode, claude]);

        Assert.False(ClosingMark.Exists(opencode.Worktree));
        Assert.True(ClosingMark.Exists(claude.Worktree));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Restoring_a_claude_agent_clears_its_mark_only_when_status_hooks_are_off(bool statusHooks, bool markKept)
    {
        var claude = Agent("cl");
        ClosingMark.Write(claude.Worktree);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = _root });

        await new RestoreSessionHandler(_mux, statusHooks: statusHooks).HandleAsync("techweb", _root, [claude]);

        Assert.Equal(markKept, ClosingMark.Exists(claude.Worktree));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Opening_a_claude_orchestrator_clears_its_mark_only_when_status_hooks_are_off(bool statusHooks, bool markKept)
    {
        var sub = Agent("lead", AgentHarness.Orchestrator);
        await SpawnStaleBrowserAsync(sub);

        var result = await new OpenAgentHandler(_mux, _store, statusHooks: statusHooks).HandleAsync("techweb", sub, _root);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(markKept, ClosingMark.Exists(sub.Worktree));
    }

    [Fact]
    public async Task Restoring_an_nvim_agent_that_runs_as_claude_because_agents_in_nvim_is_off_keeps_the_mark()
    {
        var agent = Agent("nv", AgentHarness.Nvim);
        ClosingMark.Write(agent.Worktree);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = _root });

        await new RestoreSessionHandler(_mux, agentsInNvim: false).HandleAsync("techweb", _root, [agent]);

        Assert.True(ClosingMark.Exists(agent.Worktree));
    }

    [Fact]
    public async Task Opening_an_nvim_agent_that_runs_as_claude_because_agents_in_nvim_is_off_keeps_the_mark()
    {
        var agent = Agent("nv", AgentHarness.Nvim);
        ClosingMark.Write(agent.Worktree);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = _root });

        var result = await new OpenAgentHandler(_mux, _store, agentsInNvim: false).HandleAsync("techweb", agent, _root);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(ClosingMark.Exists(agent.Worktree));
    }

    private async Task SpawnStaleBrowserAsync(AgentRecord sub)
    {
        var browser = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = sub.Worktree, Args = AgentHarness.BrowseCommandFor(SubBrowse.Title(sub)) });
        await _mux.SetTitleAsync(browser, AgentTitle.For(sub.Repository, sub.Branch));
    }

    private sealed class MemoryStore : IAgentStore
    {
        public void Save(string project, AgentRecord agent)
        {
        }

        public IReadOnlyList<AgentRecord> List(string project) => [];

        public bool Replace(string project, AgentRecord agent) => true;

        public bool Update(string project, string worktree, Func<AgentRecord, AgentRecord> change) => true;

        public bool Remove(string project, string worktree) => true;
    }
}
