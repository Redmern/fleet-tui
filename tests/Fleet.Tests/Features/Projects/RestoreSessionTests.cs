using Fleet.Features.Projects.RestoreSession;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
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

    [Fact]
    public void With_sub_orchestrators_in_nvim_off_a_sub_orchestrator_is_restored_as_claude_continue()
    {
        var worktree = Path.Combine(ProjectRoot, "orchestrations", "sub");
        Directory.CreateDirectory(worktree);
        var sub = new AgentRecord(
            worktree, "orchestrations", "sub", AgentHarness.Orchestrator, string.Empty, false, false, true);

        var options = RestoreSessionHandler.Options("techweb", sub, "w7", subOrchestratorsInNvim: false);

        Assert.Equal([AgentHarness.Claude, AgentHarness.ResumeArgument], options.Args);
        Assert.Equal(AgentHarness.SessionPersistence, options.Env);
        Assert.Equal(worktree, options.Cwd);
    }

    [Fact]
    public void Sub_orchestrators_in_nvim_off_leaves_ordinary_agents_alone()
    {
        var options = RestoreSessionHandler.Options(
            "techweb", Agent("dev", open: true), "w7", subOrchestratorsInNvim: false);

        Assert.Equal(AgentHarness.CommandFor(AgentHarness.Nvim), options.Args);
        Assert.Empty(options.Env);
    }

    [Fact]
    public async Task Visible_agents_open_one_at_a_time_in_order_while_the_titles_are_set_alongside()
    {
        Directory.CreateDirectory(ProjectRoot);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });
        var slow = new SlowMux(_mux);
        var agents = new[] { Agent("c", open: true), Agent("a", open: true), Agent("b", open: true) };

        var restored = await new RestoreSessionHandler(slow).HandleAsync("techweb", ProjectRoot, agents);

        Assert.Equal(3, restored);
        Assert.Equal(agents.Select(a => a.Worktree), slow.SpawnedCwds);
        Assert.Equal(1, slow.MostVisibleAtOnce);
        Assert.True(slow.SpawnedWhileTitling, "a title should be set while the next agent opens");

        var panes = await _mux.ListPanesAsync();
        Assert.All(agents, a => Assert.Equal(
            AgentTitle.For(a.Repository, a.Branch),
            _mux.TitleOf(panes.Single(p => p.Cwd == a.Worktree).Id)));
    }

    [Fact]
    public async Task Hidden_agents_open_side_by_side_after_the_first_but_no_more_than_four_at_once()
    {
        Directory.CreateDirectory(ProjectRoot);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });
        var slow = new SlowMux(_mux);
        var agents = Enumerable.Range(0, 9).Select(i => Agent($"h{i}", open: true, hidden: true)).ToList();

        var restored = await new RestoreSessionHandler(slow).HandleAsync("techweb", ProjectRoot, agents);

        // Side by side is shown by overlap, not by wall-clock time, which a busy CI runner stretches.
        Assert.Equal(9, restored);
        Assert.Equal(agents[0].Worktree, slow.SpawnedCwds[0]);
        Assert.False(slow.FirstHiddenOverlapped);
        Assert.InRange(slow.MostHiddenAtOnce, 2, 4);
    }

    private sealed class SlowMux(FakeMuxDriver inner) : IMuxDriver
    {
        private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(60);
        private readonly Lock _gate = new();
        private readonly List<string> _spawned = [];
        private int _visible;
        private int _hidden;
        private int _titling;
        private int _hiddenStarted;
        private bool _firstRunning;

        public IReadOnlyList<string> SpawnedCwds
        {
            get
            {
                lock (_gate)
                {
                    return [.. _spawned];
                }
            }
        }

        public int MostVisibleAtOnce { get; private set; }

        public int MostHiddenAtOnce { get; private set; }

        public bool SpawnedWhileTitling { get; private set; }

        public bool FirstHiddenOverlapped { get; private set; }

        public string Name => inner.Name;

        public MuxCaps Caps => inner.Caps;

        public PaneId CurrentPane => inner.CurrentPane;

        public async Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
        {
            var hidden = options.NewWindow;
            bool first;

            lock (_gate)
            {
                _spawned.Add(options.Cwd ?? string.Empty);
                SpawnedWhileTitling |= _titling > 0;
                first = hidden && _hiddenStarted++ == 0;
                FirstHiddenOverlapped |= hidden && !first && _firstRunning;
                _firstRunning |= first;

                if (hidden)
                {
                    MostHiddenAtOnce = Math.Max(MostHiddenAtOnce, ++_hidden);
                }
                else
                {
                    MostVisibleAtOnce = Math.Max(MostVisibleAtOnce, ++_visible);
                }
            }

            await Task.Delay(Pause, ct);

            lock (_gate)
            {
                _firstRunning &= !first;

                if (hidden)
                {
                    _hidden--;
                }
                else
                {
                    _visible--;
                }

            }

            return await inner.SpawnAsync(options, ct);
        }

        public async Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _titling);

            try
            {
                await Task.Delay(Pause * 2, ct);
                await inner.SetTitleAsync(id, title, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _titling);
            }
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => inner.IsAvailableAsync(ct);

        public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default) => inner.ListPanesAsync(ct);

        public Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default) => inner.SplitAsync(options, ct);

        public Task<PaneId> SpawnFloatingAsync(PaneId over, SpawnOptions options, CancellationToken ct = default) => inner.SpawnFloatingAsync(over, options, ct);

        public Task KillPaneAsync(PaneId id, CancellationToken ct = default) => inner.KillPaneAsync(id, ct);

        public Task MovePaneAsync(PaneId id, MovePaneOptions options, CancellationToken ct = default) => inner.MovePaneAsync(id, options, ct);

        public Task FocusPaneAsync(PaneId id, CancellationToken ct = default) => inner.FocusPaneAsync(id, ct);

        public Task SendTextAsync(PaneId id, string text, CancellationToken ct = default) => inner.SendTextAsync(id, text, ct);

        public Task<string> GetTextAsync(PaneId id, CancellationToken ct = default) => inner.GetTextAsync(id, ct);

        public Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken ct = default) => inner.ListWorkspacesAsync(ct);

        public Task ShowWorkspaceAsync(string name, CancellationToken ct = default) => inner.ShowWorkspaceAsync(name, ct);

        public Task CloseWorkspaceAsync(string name, CancellationToken ct = default) => inner.CloseWorkspaceAsync(name, ct);

        public Task OpenWindowAsync(string name, CancellationToken ct = default) => inner.OpenWindowAsync(name, ct);
    }

    [Fact]
    public async Task The_handler_spawns_a_sub_orchestrator_as_claude_continue_when_the_setting_is_off()
    {
        var worktree = Path.Combine(ProjectRoot, "orchestrations", "sub2");
        Directory.CreateDirectory(worktree);
        var sub = new AgentRecord(
            worktree, "orchestrations", "sub2", AgentHarness.Orchestrator, string.Empty, false, false, true);
        var mux = new FakeMuxDriver();

        await new RestoreSessionHandler(mux, subOrchestratorsInNvim: false).HandleAsync("techweb", ProjectRoot, [sub]);

        var pane = Assert.Single(await mux.ListPanesAsync());
        Assert.Equal([AgentHarness.Claude, AgentHarness.ResumeArgument], mux.ArgsFor(pane.Id));
        Assert.Equal(AgentHarness.SessionPersistence, mux.EnvFor(pane.Id));
    }
}
