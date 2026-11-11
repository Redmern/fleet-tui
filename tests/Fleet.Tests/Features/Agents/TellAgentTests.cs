using Fleet.Features.Agents.OpenAgent;
using Fleet.Features.Agents.TellAgent;
using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Platform.Harness;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Settings;
using Fleet.Shared.Constants;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Agents;

public sealed class TellAgentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly MemoryStore _store = new();

    private readonly MutableSettings _settings = new();

    public TellAgentTests() => Directory.CreateDirectory(_root);

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

    private TellAgentHandler Teller => new(_mux, TimeSpan.Zero);

    private async Task<(AgentRecord Sub, PaneId Pane)> DispatchAsync(bool subsInNvim)
    {
        _settings.Config = SettingsConfig.Default.WithSubOrchestratorsInNvim(subsInNvim);

        var reply = await new DispatchHandler(
                _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, settings: _settings)
            .HandleAsync(new DispatchCommand("techweb", _root, "do the thing", string.Empty), "t");

        Assert.True(reply.Succeeded, reply.Error);

        var pane = Assert.Single(await _mux.ListPanesAsync()).Id;

        return (_store.Single(reply.Value!.Folder), pane);
    }

    private int Prompts(PaneId pane) =>
        _mux.SentTo(pane).Count(t => t == AgentHarness.AgentInstructionPrompt);

    private static string Inbox(AgentRecord agent) =>
        File.ReadAllText(Path.Combine(agent.Worktree, ".fleet", AgentHarness.AgentInstructionFile));

    [Fact]
    public async Task A_sub_started_bare_still_gets_the_prompt_typed_once_after_the_setting_flips_to_nvim()
    {
        var (sub, pane) = await DispatchAsync(subsInNvim: false);

        _settings.Config = SettingsConfig.Default.WithSubOrchestratorsInNvim(true);

        await Teller.DeliverAsync(sub, pane, "review the PR");

        Assert.False(sub.StartedInNvim);
        Assert.Equal(1, Prompts(pane));
        Assert.Equal("review the PR", Inbox(sub));
    }

    [Fact]
    public async Task A_sub_started_in_nvim_is_left_to_its_pump_after_the_setting_flips_to_bare()
    {
        var (sub, pane) = await DispatchAsync(subsInNvim: true);

        _settings.Config = SettingsConfig.Default.WithSubOrchestratorsInNvim(false);

        await Teller.DeliverAsync(sub, pane, "review the PR");

        Assert.True(sub.StartedInNvim);
        Assert.Equal(0, Prompts(pane));
        Assert.Empty(_mux.SentTo(pane));
        Assert.Equal("review the PR", Inbox(sub));
    }

    [Fact]
    public async Task A_restored_sub_is_told_the_way_it_was_restored_not_the_way_it_was_dispatched()
    {
        var (sub, pane) = await DispatchAsync(subsInNvim: true);
        await _mux.KillPaneAsync(pane);

        await new RestoreSessionHandler(_mux, subOrchestratorsInNvim: false, _store)
            .HandleAsync("techweb", _root, [sub]);

        var restored = _store.Single(sub.Worktree);
        var again = Assert.Single(await _mux.ListPanesAsync()).Id;

        await Teller.DeliverAsync(restored, again, "carry on");

        Assert.False(restored.StartedInNvim);
        Assert.Equal(1, Prompts(again));
    }

    [Fact]
    public async Task A_reopened_sub_is_told_the_way_it_was_reopened()
    {
        var (sub, pane) = await DispatchAsync(subsInNvim: false);
        await _mux.KillPaneAsync(pane);

        Assert.True((await new OpenAgentHandler(_mux, _store, subOrchestratorsInNvim: true)
            .HandleAsync("techweb", sub, _root)).Succeeded);

        var reopened = _store.Single(sub.Worktree);
        var again = Assert.Single(await _mux.ListPanesAsync()).Id;

        await Teller.DeliverAsync(reopened, again, "carry on");

        Assert.True(reopened.StartedInNvim);
        Assert.Equal(0, Prompts(again));
    }

    [Fact]
    public void A_sub_recorded_before_the_host_was_stored_counts_as_nvim()
    {
        var legacy = new AgentRecord(_root, "orchestrations", "old", AgentHarness.Orchestrator, string.Empty, false);

        Assert.Null(legacy.InNvim);
        Assert.True(TellAgentHandler.PumpedByNvim(legacy));
    }

    [Fact]
    public void An_nvim_agent_started_as_bare_claude_is_told_by_typing()
    {
        var agent = new AgentRecord(_root, "backend", "a", AgentHarness.Nvim, string.Empty, false, InNvim: false);

        Assert.False(TellAgentHandler.PumpedByNvim(agent));
        Assert.True(TellAgentHandler.PumpedByNvim(agent with { InNvim = null }));
    }

    [Fact]
    public async Task Ordinary_agents_are_told_by_their_harness_as_before()
    {
        var claude = new AgentRecord(_root, "backend", "a", AgentHarness.Claude, string.Empty, false);
        var nvim = claude with { Harness = AgentHarness.Nvim, Branch = "b" };
        var claudePane = await _mux.SpawnAsync(new() { Cwd = _root, NewWindow = true });
        var nvimPane = await _mux.SpawnAsync(new() { Cwd = _root });

        await Teller.DeliverAsync(claude, claudePane, "x");
        await Teller.DeliverAsync(nvim, nvimPane, "y");

        Assert.Equal([AgentHarness.AgentInstructionPrompt, "\r"], _mux.SentTo(claudePane));
        Assert.Empty(_mux.SentTo(nvimPane));
    }

    [Fact]
    public async Task An_agent_with_an_inbox_is_handed_to_SendMessage_and_nothing_is_typed_or_written()
    {
        var agent = new AgentRecord(_root, "backend", "a", AgentHarness.Claude, string.Empty, false);
        var pane = await _mux.SpawnAsync(new() { Cwd = _root, NewWindow = true });
        var teller = new TellAgentHandler(_mux, TimeSpan.Zero, new FixedInboxes(_root, "uds:pipe-a"));

        var address = await teller.RouteAsync(agent, pane, "review the PR", typed: false);

        Assert.Equal("uds:pipe-a", address);
        Assert.Empty(_mux.SentTo(pane));
        Assert.False(File.Exists(Path.Combine(_root, ".fleet", AgentHarness.AgentInstructionFile)));
    }

    [Fact]
    public async Task Without_an_inbox_or_when_asked_to_type_the_agent_is_told_through_its_pane()
    {
        var agent = new AgentRecord(_root, "backend", "a", AgentHarness.Claude, string.Empty, false);
        var pane = await _mux.SpawnAsync(new() { Cwd = _root, NewWindow = true });
        var elsewhere = new TellAgentHandler(_mux, TimeSpan.Zero, new FixedInboxes("C:\\other", "uds:pipe-b"));
        var inbox = new TellAgentHandler(_mux, TimeSpan.Zero, new FixedInboxes(_root, "uds:pipe-a"));

        Assert.Null(await elsewhere.RouteAsync(agent, pane, "x", typed: false));
        Assert.Null(await inbox.RouteAsync(agent, pane, "y", typed: true));
        Assert.Null(await Teller.RouteAsync(agent, pane, "z", typed: false));

        Assert.Equal(3, Prompts(pane));
        Assert.Equal($"z\n\n{AgentHarness.AgentReportRule}", Inbox(agent));
    }

    [Theory]
    [InlineData(AgentHarness.Claude, AgentHarness.Claude, true)]
    [InlineData(AgentHarness.Claude, AgentHarness.Opencode, false)]
    [InlineData(AgentHarness.Opencode, AgentHarness.Claude, false)]
    [InlineData(AgentHarness.Opencode, AgentHarness.Opencode, false)]
    public async Task Only_a_claude_caller_telling_a_claude_agent_gets_a_SendMessage_address(
        string caller, string target, bool native)
    {
        var agent = new AgentRecord(_root, "backend", "a", target, string.Empty, false);
        var pane = await _mux.SpawnAsync(new() { Cwd = _root, NewWindow = true });
        var teller = new TellAgentHandler(_mux, TimeSpan.Zero, new FixedInboxes(_root, "uds:pipe-a"));

        var address = await teller.RouteAsync(agent, pane, "review the PR", typed: false, callerHarness: caller);

        Assert.Equal(native ? "uds:pipe-a" : null, address);
        Assert.Equal(native ? 0 : 1, Prompts(pane));
        Assert.Equal(!native, File.Exists(Path.Combine(_root, ".fleet", AgentHarness.AgentInstructionFile)));
    }

    [Fact]
    public async Task An_opencode_caller_types_into_a_claude_sub_orchestrator()
    {
        var sub = new AgentRecord(_root, string.Empty, "sub", AgentHarness.Orchestrator, string.Empty, false, InNvim: false);
        var pane = await _mux.SpawnAsync(new() { Cwd = _root, NewWindow = true });
        var teller = new TellAgentHandler(_mux, TimeSpan.Zero, new FixedInboxes(_root, "uds:sub"));

        Assert.Null(await teller.RouteAsync(sub, pane, "merge it", typed: false, callerHarness: AgentHarness.Opencode));
        Assert.Equal(1, Prompts(pane));
        Assert.Equal("merge it", Inbox(sub));
    }

    [Fact]
    public async Task A_claude_caller_types_into_an_opencode_sub_orchestrator_even_when_it_has_an_inbox()
    {
        var sub = new AgentRecord(
            _root, string.Empty, "sub", AgentHarness.Orchestrator, string.Empty, false, InNvim: false,
            OrchestratorHarness: AgentHarness.Opencode);
        var pane = await _mux.SpawnAsync(new() { Cwd = _root, NewWindow = true });
        var teller = new TellAgentHandler(_mux, TimeSpan.Zero, new FixedInboxes(_root, "uds:sub"));

        Assert.Equal(AgentHarness.Opencode, TellAgentHandler.HarnessOf(sub));
        Assert.Null(await teller.RouteAsync(sub, pane, "merge it", typed: false, callerHarness: AgentHarness.Claude));
        Assert.Equal(1, Prompts(pane));
        Assert.Equal("merge it", Inbox(sub));
    }

    [Fact]
    public async Task A_first_task_an_opencode_agent_dropped_is_typed_again_before_enter()
    {
        var agent = new AgentRecord(_root, "backend", "a", AgentHarness.Opencode, string.Empty, false);
        var pane = await _mux.SpawnAsync(new() { Cwd = _root, NewWindow = true });

        await Teller.DeliverAsync(agent, pane, "fix it", echoWithin: TimeSpan.FromMilliseconds(1300));

        Assert.True(Prompts(pane) >= 2, string.Join(" | ", _mux.SentTo(pane)));
        Assert.Equal("\r", _mux.SentTo(pane)[^1]);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("pi")]
    public async Task A_claude_caller_types_into_an_agent_of_another_harness(string harness)
    {
        var agent = new AgentRecord(_root, "backend", "a", harness, string.Empty, false);
        var pane = await _mux.SpawnAsync(new() { Cwd = _root, NewWindow = true });
        var teller = new TellAgentHandler(_mux, TimeSpan.Zero, new FixedInboxes(_root, "uds:pipe-a"));

        Assert.Null(await teller.RouteAsync(agent, pane, "review the PR", typed: false, callerHarness: AgentHarness.Claude));
        Assert.Equal([AgentHarness.AgentInstructionPrompt, "\r"], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_follow_up_to_an_opencode_agent_is_typed_once()
    {
        var agent = new AgentRecord(_root, "backend", "a", AgentHarness.Opencode, string.Empty, false);
        var pane = await _mux.SpawnAsync(new() { Cwd = _root, NewWindow = true });

        await Teller.DeliverAsync(agent, pane, "fix it");

        Assert.Equal([AgentHarness.AgentInstructionPrompt, "\r"], _mux.SentTo(pane));
    }

    [Fact]
    public void A_record_names_the_harness_its_pane_runs()
    {
        var nvimClaude = new AgentRecord(_root, "backend", "a", AgentHarness.Claude, string.Empty, false, HostInNvim: true);

        Assert.Equal(AgentHarness.Claude, TellAgentHandler.HarnessOf(nvimClaude));
        Assert.Equal(AgentHarness.Opencode, TellAgentHandler.HarnessOf(nvimClaude with { Harness = AgentHarness.Opencode }));
        Assert.Equal(AgentHarness.Claude, TellAgentHandler.HarnessOf(nvimClaude with { Harness = AgentHarness.Orchestrator }));
        Assert.Equal(AgentHarness.Nvim, TellAgentHandler.HarnessOf(nvimClaude with { Harness = AgentHarness.Nvim, HostInNvim = false }));
    }

    [Fact]
    public void A_repo_agent_is_told_to_report_with_every_instruction_and_a_sub_is_not()
    {
        var repo = new AgentRecord(_root, "backend", "a", AgentHarness.Claude, string.Empty, false);
        var sub = repo with { Harness = AgentHarness.Orchestrator };

        Assert.Equal($"fix it\n\n{AgentHarness.AgentReportRule}", TellAgentHandler.InstructionFor(repo, "fix it"));
        Assert.Equal("fix it", TellAgentHandler.InstructionFor(sub, "fix it"));
    }

    private sealed class FixedInboxes(string folder, string address) : IAgentInboxes
    {
        public Task<string?> AddressAsync(string target, CancellationToken ct = default) =>
            Task.FromResult(target == folder ? address : null);
    }

    private sealed class MemoryStore : IAgentStore
    {
        private readonly List<AgentRecord> _agents = [];

        public AgentRecord Single(string worktree) => _agents.Single(a => a.Worktree == worktree);

        public void Save(string project, AgentRecord agent)
        {
            _agents.RemoveAll(a => a.Worktree == agent.Worktree);
            _agents.Add(agent);
        }

        public IReadOnlyList<AgentRecord> List(string project) => _agents;

        public bool Replace(string project, AgentRecord agent)
        {
            Save(project, agent);
            return true;
        }

        public bool Remove(string project, string worktree)
        {
            _agents.RemoveAll(a => a.Worktree == worktree);
            return true;
        }
    }

    private sealed class MutableSettings : ISettingsStore
    {
        public SettingsConfig Config { get; set; } = SettingsConfig.Default;

        public SettingsConfig Load(string project) => Config;

        public void Save(string project, SettingsConfig config) => Config = config;
    }
}
