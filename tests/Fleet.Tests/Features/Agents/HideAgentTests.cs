using Fleet.Features.Agents;
using Fleet.Features.Agents.ChangeHarness;
using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Ui;

namespace Fleet.Tests.Features.Agents;

public class HideAgentTests
{
    private readonly FakeMuxDriver _mux = new();

    private readonly RecordingStore _store = new();

    private static AgentRecord Agent(bool hidden = false) =>
        new("C:/repos/techweb/backend/test", "backend", "test", AgentHarness.Claude,
            "origin/main", true, hidden);

    [Fact]
    public async Task Hiding_moves_the_pane_into_the_hidden_workspace()
    {
        var agent = Agent();
        var pane = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        var result = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, dashboardWindow: "w1");

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.Hidden);

        var panes = await _mux.ListPanesAsync();
        Assert.Equal(FleetWorkspaces.Hidden, panes.Single(p => p.Id == pane).SessionName);
    }

    [Fact]
    public async Task Moving_the_pane_names_its_new_tab_after_the_branch_again()
    {
        var agent = Agent();
        var pane = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        await new HideAgentHandler(_mux, _store).HandleAsync("techweb", agent, "w1");

        Assert.Equal("backend/test", _mux.TitleOf(pane));
    }

    [Fact]
    public async Task A_second_hidden_agent_joins_the_first_ones_hidden_window()
    {
        var first = Agent();
        var second = new AgentRecord(
            "C:/repos/techweb/backend/other", "backend", "other", AgentHarness.Claude,
            "origin/main", true);

        var firstPane = await _mux.SpawnAsync(new SpawnOptions { Cwd = first.Worktree });
        var secondPane = await _mux.SpawnAsync(new SpawnOptions { Cwd = second.Worktree });

        var handler = new HideAgentHandler(_mux, _store);

        Assert.True((await handler.HandleAsync("techweb", first, "w1")).Succeeded);
        Assert.True((await handler.HandleAsync("techweb", second, "w1")).Succeeded);

        var panes = await _mux.ListPanesAsync();
        var one = panes.Single(p => p.Id == firstPane);
        var two = panes.Single(p => p.Id == secondPane);

        Assert.Equal(FleetWorkspaces.Hidden, one.SessionName);
        Assert.Equal(FleetWorkspaces.Hidden, two.SessionName);
        Assert.Equal(one.WindowId, two.WindowId);
    }

    [Fact]
    public async Task A_hidden_agent_is_still_recorded_so_the_dashboard_keeps_listing_it()
    {
        var agent = Agent();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        await new HideAgentHandler(_mux, _store).HandleAsync("techweb", agent, "w1");

        var saved = Assert.Single(_store.Saved);

        Assert.True(saved.Hidden);
        Assert.Equal(agent.Worktree, saved.Worktree);
    }

    [Fact]
    public async Task Showing_a_hidden_agent_moves_it_back_to_the_dashboards_window()
    {
        var agent = Agent(hidden: true);
        var pane = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = agent.Worktree, Workspace = FleetWorkspaces.Hidden });

        var result = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, dashboardWindow: "w9");

        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.Value!.Hidden);

        var panes = await _mux.ListPanesAsync();
        Assert.Equal("w9", panes.Single(p => p.Id == pane).WindowId);
    }

    [Fact]
    public async Task Hiding_keeps_the_agent_marked_open_because_its_pane_lives_on()
    {
        var agent = Agent();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        await new HideAgentHandler(_mux, _store).HandleAsync("techweb", agent, "w1");

        var saved = Assert.Single(_store.Saved);

        Assert.True(saved.Hidden);
        Assert.True(saved.Open);
    }

    [Fact]
    public async Task Hiding_a_sub_drops_its_browser_and_moves_claude_to_hidden_keeping_focus()
    {
        var agent = Agent() with { Harness = AgentHarness.Orchestrator };
        var claude = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = agent.Worktree, NewWindow = true });
        await _mux.SetTitleAsync(claude, AgentTitle.For(agent.Repository, agent.Branch));
        await SubBrowse.SplitAsync(_mux, agent, claude);
        var browser = (await _mux.ListPanesAsync()).Single(p => p.Id != claude).Id;
        var dashboard = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = "C:/repos/techweb", NewWindow = true });
        var home = (await _mux.ListPanesAsync()).Single(p => p.Id == dashboard).WindowId;

        await _mux.FocusPaneAsync(dashboard);

        var result = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, dashboardWindow: home);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.Hidden);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(FleetWorkspaces.Hidden, panes.Single(p => p.Id == claude).SessionName);
        Assert.DoesNotContain(panes, p => p.Id == browser);
        Assert.True(panes.Single(p => p.Id == dashboard).IsActive);
        Assert.False(panes.Single(p => p.Id == claude).IsActive);
    }

    [Fact]
    public async Task Hiding_a_sub_whose_browser_shares_its_tab_keeps_claude_alive()
    {
        var agent = Agent() with { Harness = AgentHarness.Orchestrator };
        var claude = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree, NewWindow = true });
        await _mux.SetTitleAsync(claude, AgentTitle.For(agent.Repository, agent.Branch));
        await SubBrowse.SplitAsync(_mux, agent, claude);
        var browser = (await _mux.ListPanesAsync()).Single(p => p.Id != claude).Id;

        var result = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, dashboardWindow: "w9");

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(FleetWorkspaces.Hidden, panes.Single(p => p.Id == claude).SessionName);
        Assert.DoesNotContain(panes, p => p.Id == browser);
        Assert.True(result.Value!.Open);
    }

    [Fact]
    public async Task Unhiding_a_sub_puts_a_browser_beside_claude_and_names_the_tab_after_the_sub()
    {
        var agent = Agent(hidden: true) with { Harness = AgentHarness.Orchestrator };
        var dashboard = await _mux.SpawnAsync(new SpawnOptions { Cwd = "C:/repos/techweb", NewWindow = true });
        var home = (await _mux.ListPanesAsync()).Single(p => p.Id == dashboard).WindowId;
        var claude = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = agent.Worktree, Workspace = FleetWorkspaces.Hidden, NewWindow = true });

        var result = await new HideAgentHandler(_mux, _store).HandleAsync("techweb", agent, home);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();
        var mine = panes.Single(p => p.Id == claude);
        var browser = Assert.Single(panes, p => SubBrowse.Is(p));

        Assert.Equal(home, mine.WindowId);
        Assert.Equal(mine.TabId, browser.TabId);
        Assert.Equal("backend/test", mine.Title);
        Assert.Equal("backend/test", browser.Title);
        Assert.Equal("backend/test files", browser.PaneTitle);
    }

    [Fact]
    public async Task An_agent_with_no_pane_is_recorded_as_closed()
    {
        var saved = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", Agent() with { Open = true }, "w1");

        Assert.False(saved.Value!.Open);
    }

    [Fact]
    public async Task An_agent_with_no_pane_still_records_the_change()
    {
        var result = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", Agent(), "w1");

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.Hidden);
        Assert.Single(_store.Saved);
    }

    [Fact]
    public void Changing_the_harness_records_it_against_the_same_agent()
    {
        var changed = new ChangeHarnessHandler(_store)
            .Handle("techweb", Agent(), AgentHarness.Nvim);

        Assert.True(changed.Succeeded, changed.Error);
        Assert.Equal(AgentHarness.Nvim, changed.Value!.Harness);
        Assert.Equal(AgentHarness.Nvim, Assert.Single(_store.Saved).Harness);
    }

    [Fact]
    public void Choosing_the_harness_it_already_has_writes_nothing()
    {
        var changed = new ChangeHarnessHandler(_store)
            .Handle("techweb", Agent(), AgentHarness.Claude);

        Assert.True(changed.Succeeded, changed.Error);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void An_unknown_harness_falls_back_to_claude_rather_than_being_stored()
    {
        var changed = new ChangeHarnessHandler(_store)
            .Handle("techweb", Agent() with { Harness = AgentHarness.Nvim }, "emacs");

        Assert.Equal(AgentHarness.Nvim, changed.Value!.Harness);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<AgentRecord> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Saved;

        public void Remove(string project, string worktree) { }
    }

    [Fact]
    public void Claude_runs_on_its_own()
    {
        Assert.Equal([AgentHarness.Claude], AgentHarness.CommandFor(AgentHarness.Claude));
    }

    [Fact]
    public void Nvim_is_started_with_neo_tree_and_claude_closed()
    {
        var command = AgentHarness.CommandFor(AgentHarness.Nvim);

        Assert.Equal(AgentHarness.Nvim, command[0]);
        Assert.Equal("-c", command[1]);
        Assert.Contains("Neotree show", command[2]);
        Assert.DoesNotContain("ClaudeCode", command[2]);
    }

    [Fact]
    public void Nvim_launches_claude_only_when_asked_for_an_orchestrator_child()
    {
        Assert.Contains("ClaudeCode", AgentHarness.CommandFor(AgentHarness.Nvim, withClaude: true)[2]);
        Assert.DoesNotContain("ClaudeCode", AgentHarness.CommandFor(AgentHarness.Nvim)[2]);
    }

    [Fact]
    public void The_startup_commands_are_scheduled_so_lazy_plugins_have_loaded()
    {
        Assert.Contains("vim.schedule(", AgentHarness.NvimStartup);
    }

    [Fact]
    public void Startup_drops_to_normal_mode_after_opening_claude()
    {
        Assert.Contains("stopinsert", AgentHarness.NvimStartup);
    }

    [Fact]
    public void Nvim_forces_session_persistence_for_the_claude_it_launches()
    {
        Assert.Contains("CLAUDE_CODE_FORCE_SESSION_PERSISTENCE='1'", AgentHarness.NvimStartup);
        Assert.Contains("CLAUDE_CODE_CHILD_SESSION=nil", AgentHarness.NvimStartup);
    }

    [Fact]
    public void Nvim_watches_the_instruction_file_instead_of_trusting_keystrokes()
    {
        Assert.Contains("getftime('.fleet/instruction.md')", AgentHarness.NvimStartup);
        Assert.Contains("instruction.seen", AgentHarness.NvimStartup);
        Assert.Contains(AgentHarness.AgentInstructionPrompt, AgentHarness.NvimStartup);
        Assert.Contains("fleet_timer:start(3000, 3000", AgentHarness.NvimStartup);
        Assert.Contains("(vim.uv or vim.loop).new_timer()", AgentHarness.NvimStartup);
    }

    [Fact]
    public void FleetTell_sends_the_enter_separately_so_claude_submits_it()
    {
        Assert.Contains("vim.fn.chansend(c, o.args)", AgentHarness.NvimStartup);
        Assert.Contains("vim.defer_fn(", AgentHarness.NvimStartup);
        Assert.Contains("vim.fn.chansend(c, '\\r')", AgentHarness.NvimStartup);
        Assert.DoesNotContain("o.args..'\\r'", AgentHarness.NvimStartup);
    }

    [Fact]
    public void An_nvim_agent_is_not_shell_wrapped_since_its_lua_sets_the_env()
    {
        Assert.Empty(AgentHarness.SpawnEnv(AgentHarness.Nvim));
        Assert.NotEmpty(AgentHarness.SpawnEnv(AgentHarness.Claude));
    }

    [Fact]
    public void An_unknown_harness_falls_back_to_the_default_which_is_nvim()
    {
        Assert.Equal(AgentHarness.CommandFor(AgentHarness.Nvim), AgentHarness.CommandFor("emacs"));
    }

    [Fact]
    public void Nvim_is_the_default_and_is_labelled_plainly()
    {
        Assert.Equal(AgentHarness.Nvim, AgentHarness.All[0]);
        Assert.Equal("nvim", AgentHarness.Describe(AgentHarness.Nvim));
        Assert.Equal("claude", AgentHarness.Describe(AgentHarness.Claude));
    }

    [Fact]
    public void Browsing_a_repository_opens_nvim_with_the_tree_but_not_claude()
    {
        Assert.Equal(AgentHarness.Nvim, AgentHarness.BrowseCommand[0]);
        Assert.Contains("Neotree show", AgentHarness.BrowseStartup);
        Assert.DoesNotContain("ClaudeCode", AgentHarness.BrowseStartup);
    }

    [Fact]
    public void The_manage_menu_offers_every_per_agent_action()
    {
        Assert.Equal(5, AgentDisposal.Choices.Count);
        Assert.Contains("opens", AgentDisposal.Choices[AgentDisposal.Opens]);
        Assert.Contains("Hide it", AgentDisposal.Choices[AgentDisposal.Hide]);
        Assert.Contains("Stop", AgentDisposal.Choices[AgentDisposal.Stop]);
        Assert.Contains("keep its files", AgentDisposal.Choices[AgentDisposal.Forget]);
        Assert.Contains("delete its worktree", AgentDisposal.Choices[AgentDisposal.Delete]);
    }

    [Fact]
    public void Every_manage_choice_pairs_a_keyword_with_its_description()
    {
        Assert.Equal(
            ["opens", "finish", "hide", "rename", "stop", "forget", "delete"],
            AgentDisposal.Entries.Select(e => e.Label));

        Assert.All(AgentDisposal.Entries, e => Assert.NotEqual(0, e.Detail.Length));
    }

    [Fact]
    public void A_hidden_agent_is_offered_show_rather_than_hide()
    {
        var hidden = AgentDisposal.For(hidden: true);

        Assert.Equal(AgentWords.Show, hidden[2].Label);
        Assert.Equal(AgentDisposal.ShowDetail, hidden[2].Detail);

        Assert.Equal(AgentWords.Hide, AgentDisposal.For(hidden: false)[2].Label);
    }

    [Fact]
    public void The_manage_keys_are_pinned_so_show_does_not_steal_stops_key()
    {
        foreach (var hidden in new[] { true, false })
        {
            Assert.Equal(
                ["o", "p", "h", "r", "s", "f", "d"],
                PickerKeys.For(AgentDisposal.For(hidden)));
        }
    }
}
