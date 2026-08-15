using Fleet.Features.Agents.ChangeHarness;
using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
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

        Assert.Equal("test", _mux.TitleOf(pane));
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
    public async Task Hiding_an_orchestrator_leaves_its_split_intact_and_steps_back_to_the_dashboard()
    {
        var agent = Agent() with { Harness = AgentHarness.Orchestrator };
        var claude = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = agent.Worktree, NewWindow = true });
        var browser = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        var dashboard = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = "C:/repos/techweb", NewWindow = true });
        var home = (await _mux.ListPanesAsync()).Single(p => p.Id == dashboard).WindowId;

        var result = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, dashboardWindow: home);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.Hidden);

        var panes = await _mux.ListPanesAsync();

        // The sub's panes are not moved to the hidden workspace — the split survives.
        Assert.NotEqual(FleetWorkspaces.Hidden, panes.Single(p => p.Id == claude).SessionName);
        Assert.NotEqual(FleetWorkspaces.Hidden, panes.Single(p => p.Id == browser).SessionName);
        Assert.True(panes.Single(p => p.Id == dashboard).IsActive);
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
    public void Nvim_is_started_with_neo_tree_and_claude_open()
    {
        var command = AgentHarness.CommandFor(AgentHarness.Nvim);

        Assert.Equal(AgentHarness.Nvim, command[0]);
        Assert.Equal("-c", command[1]);
        Assert.Contains("Neotree show", command[2]);
        Assert.Contains("ClaudeCode", command[2]);
    }

    [Fact]
    public void The_startup_commands_are_scheduled_so_lazy_plugins_have_loaded()
    {
        Assert.StartsWith("lua vim.schedule(", AgentHarness.NvimStartup);
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
        Assert.Equal(AgentDisposal.Choices.Count, AgentDisposal.Entries.Count);

        Assert.Equal(
            ["opens", "hide", "stop", "forget", "delete"],
            AgentDisposal.Entries.Select(e => e.Label));

        Assert.Equal(AgentDisposal.Choices, AgentDisposal.Entries.Select(e => e.Detail));
    }

    [Fact]
    public void A_hidden_agent_is_offered_show_rather_than_hide()
    {
        var hidden = AgentDisposal.For(hidden: true);

        Assert.Equal(AgentWords.Show, hidden[AgentDisposal.Hide].Label);
        Assert.Equal(AgentDisposal.ShowDetail, hidden[AgentDisposal.Hide].Detail);

        Assert.Equal(AgentWords.Hide, AgentDisposal.For(hidden: false)[AgentDisposal.Hide].Label);
    }

    [Fact]
    public void The_manage_keys_are_pinned_so_show_does_not_steal_stops_key()
    {
        foreach (var hidden in new[] { true, false })
        {
            Assert.Equal(
                ["o", "h", "s", "f", "d"],
                PickerKeys.For(AgentDisposal.For(hidden)));
        }
    }
}
