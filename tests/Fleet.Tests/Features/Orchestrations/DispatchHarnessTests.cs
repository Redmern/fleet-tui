using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Platform.Harness;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Harness;
using Fleet.Ports.Mux;
using Fleet.Ports.Settings;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class DispatchHarnessTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fleet-dispatch-harness-").FullName;

    private readonly FakeMuxDriver _mux = new();

    private readonly Store _store = new();

    private readonly RecordingConfigs _configs = new();

    private readonly RecordingConfig _claude = new("claude-default");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private DispatchHandler Handler(bool subsInNvim = false, IAgentStarter? starter = null) =>
        new(
            _mux, _store, _claude, TimeSpan.Zero, TimeSpan.Zero,
            settings: new Settings(SettingsConfig.Default.WithSubOrchestratorsInNvim(subsInNvim)),
            starter: starter,
            configs: _configs);

    private DispatchCommand Command(string harness) =>
        new("techweb", _root, "do the thing") { Harness = harness };

    [Theory]
    [InlineData("opencode")]
    [InlineData("codex")]
    [InlineData("pi")]
    public async Task A_sub_dispatched_with_a_harness_gets_that_harness_instruction_file_config_and_kickoff(
        string harness)
    {
        var profile = HarnessRegistry.For(harness);

        var reply = await Handler().HandleAsync(Command(harness), "t");

        Assert.True(reply.Succeeded, reply.Error);
        var folder = reply.Value!.Folder;
        Assert.True(File.Exists(Path.Combine(folder, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(folder, "CLAUDE.md")));
        Assert.Contains(OrchestrationText.WaitingForAgentsTyped, File.ReadAllText(Path.Combine(folder, "AGENTS.md")));
        Assert.True(File.Exists(OrchestrationPaths.TaskFile(folder)));
        Assert.Equal([(harness, folder, "techweb", "do-the-thing")], _configs.Written);
        Assert.Empty(_claude.Written);
        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(folder, pane.Cwd);
        Assert.Equal([harness], _mux.ArgsFor(pane.Id));
        Assert.Empty(_mux.EnvFor(pane.Id));
        Assert.Equal([profile.Kickoff, "\r"], _mux.SentTo(pane.Id));
        Assert.Equal("Read AGENTS.md and TASK.md in your working directory, then begin.", profile.Kickoff);
    }

    [Fact]
    public async Task A_kickoff_the_harness_did_not_show_is_typed_again_once_before_enter()
    {
        var clock = new SkippingClock();
        var handler = new DispatchHandler(
            _mux, _store, _claude, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(50),
            settings: new Settings(SettingsConfig.Default.WithSubOrchestratorsInNvim(false)),
            orchestrator: HarnessRegistry.Opencode with { InputRetry = TimeSpan.FromMilliseconds(1) },
            clock: clock);
        var kickoff = HarnessRegistry.Opencode.Kickoff;

        clock.Skipped += () =>
        {
            foreach (var pane in _mux.ListPanesAsync().Result)
            {
                _mux.SetText(pane.Id, _mux.SentTo(pane.Id).Count(s => s == kickoff) == 2 ? "> " + kickoff : "opencode is starting");
            }
        };

        var reply = await handler.HandleAsync(new DispatchCommand("techweb", _root, "do the thing"), "t");

        var sent = _mux.SentTo(Assert.Single(await _mux.ListPanesAsync()).Id);
        Assert.Equal([kickoff, TypedPrompt.ClearLine, kickoff, "\r"], sent);
        Assert.DoesNotContain("not confirmed", reply.Value!.Note);
    }

    [Fact]
    public async Task A_kickoff_the_harness_never_showed_is_typed_twice_at_most_and_not_submitted()
    {
        var clock = new SkippingClock();
        var handler = new DispatchHandler(
            _mux, _store, _claude, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50),
            settings: new Settings(SettingsConfig.Default.WithSubOrchestratorsInNvim(false)),
            orchestrator: HarnessRegistry.Opencode with { InputRetry = TimeSpan.FromMilliseconds(1) },
            clock: clock);
        clock.Skipped += () =>
        {
            foreach (var pane in _mux.ListPanesAsync().Result)
            {
                _mux.SetText(pane.Id, "opencode is starting");
            }
        };

        var reply = await handler.HandleAsync(new DispatchCommand("techweb", _root, "do the thing"), "t");

        var kickoff = HarnessRegistry.Opencode.Kickoff;
        var sent = _mux.SentTo(Assert.Single(await _mux.ListPanesAsync()).Id);
        Assert.Equal([kickoff, TypedPrompt.ClearLine, kickoff, TypedPrompt.ClearLine], sent);
        Assert.Contains(
            "written to .fleet/instruction.md, but delivery was not confirmed (its pane never showed the typed prompt (a pane fleet cannot read looks the same)",
            reply.Value!.Note);
    }

    [Fact]
    public async Task A_kickoff_that_runs_out_of_time_is_typed_once_submitted_once_and_said_to_be_unconfirmed()
    {
        var reply = await Handler().HandleAsync(Command("opencode"), "t");

        var sent = _mux.SentTo(Assert.Single(await _mux.ListPanesAsync()).Id);
        Assert.Equal([HarnessRegistry.Opencode.Kickoff, "\r"], sent);
        Assert.Contains(
            "delivery was not confirmed (fleet stopped waiting for the prompt to show, before the caller's tool call would time out, and pressed Enter once)",
            reply.Value!.Note);
    }

    [Fact]
    public async Task A_claude_kickoff_is_not_doubted_once_its_mcp_server_reported_ready()
    {
        var clock = new SkippingClock();
        clock.Skipped += () =>
        {
            foreach (var file in Directory.GetFiles(_root, AgentHarness.AgentInstructionFile, SearchOption.AllDirectories))
            {
                File.WriteAllText(OrchestrationPaths.ReadyMarker(Path.GetDirectoryName(Path.GetDirectoryName(file))!), string.Empty);
            }
        };
        var handler = new DispatchHandler(
            _mux, _store, _claude, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50),
            settings: new Settings(SettingsConfig.Default.WithSubOrchestratorsInNvim(false)), clock: clock);

        var reply = await handler.HandleAsync(new DispatchCommand("techweb", _root, "do the thing"), "t");

        Assert.DoesNotContain("not confirmed", reply.Value!.Note);
    }

    [Fact]
    public async Task A_claude_kickoff_is_doubted_when_its_mcp_server_never_reported_ready()
    {
        var handler = new DispatchHandler(
            _mux, _store, _claude, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50),
            settings: new Settings(SettingsConfig.Default.WithSubOrchestratorsInNvim(false)), clock: new SkippingClock());

        var reply = await handler.HandleAsync(new DispatchCommand("techweb", _root, "do the thing"), "t");

        Assert.Contains(
            "delivery was not confirmed (its harness has no check for typed input and its MCP server never reported ready)",
            reply.Value!.Note);
    }

    // A caller with a 60 s tool limit gets its answer inside 50 s even when the sub's MCP server never reports ready.
    [Theory]
    [InlineData("opencode")]
    [InlineData("claude")]
    public async Task A_bare_sub_that_never_reports_ready_is_answered_inside_the_callers_budget(string harness)
    {
        var clock = new SkippingClock();
        var handler = new DispatchHandler(
            _mux, _store, _claude, TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(200),
            settings: new Settings(SettingsConfig.Default.WithSubOrchestratorsInNvim(false)),
            configs: _configs, clock: clock);
        clock.Skipped += () =>
        {
            foreach (var pane in _mux.ListPanesAsync().Result)
            {
                _mux.SetText(pane.Id, "starting");
            }
        };
        var started = clock.GetUtcNow();

        var reply = await handler.HandleAsync(Command(harness) with { Within = TimeSpan.FromSeconds(50) }, "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.InRange(clock.GetUtcNow() - started, TimeSpan.FromSeconds(49), TimeSpan.FromSeconds(51));
        Assert.Contains("delivery was not confirmed", reply.Value!.Note);
    }

    [Fact]
    public void Only_harnesses_without_a_ready_hook_have_their_typed_prompt_checked()
    {
        Assert.Equal(TimeSpan.Zero, HarnessRegistry.Claude.InputRetry);
        Assert.True(HarnessRegistry.Opencode.InputRetry > TimeSpan.Zero);
        Assert.True(HarnessRegistry.Codex.InputRetry > TimeSpan.Zero);
        Assert.Equal(HarnessRegistry.Opencode.InputRetry, HarnessRegistry.Pi.InputRetry);
    }

    [Fact]
    public async Task The_sub_record_stores_the_harness_it_runs()
    {
        await Handler().HandleAsync(Command("opencode"), "t");

        var record = Assert.Single(_store.Saved);
        Assert.Equal(AgentHarness.Orchestrator, record.Harness);
        Assert.Equal("opencode", record.OrchestratorHarness);
        Assert.Equal(HarnessRegistry.Opencode, record.OrchestratorProfile);
    }

    [Fact]
    public async Task With_subs_in_nvim_on_a_harness_whose_typed_kickoff_is_checked_still_starts_outside_nvim()
    {
        var reply = await Handler(subsInNvim: true).HandleAsync(Command("codex"), "t");

        Assert.True(reply.Succeeded, reply.Error);
        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(["codex"], _mux.ArgsFor(pane.Id));
        Assert.Equal([HarnessRegistry.Codex.Kickoff, "\r"], _mux.SentTo(pane.Id));
        Assert.False(Assert.Single(_store.Saved).InNvim);
    }

    [Fact]
    public async Task With_subs_in_nvim_on_a_claude_sub_is_still_hosted_in_nvim()
    {
        await Handler(subsInNvim: true).HandleAsync(Command("claude"), "t");

        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(AgentHarness.Nvim, _mux.ArgsFor(pane.Id)[0]);
        Assert.Empty(_mux.SentTo(pane.Id));
        Assert.True(Assert.Single(_store.Saved).InNvim);
    }

    [Theory]
    [InlineData("")]
    [InlineData("claude")]
    [InlineData("nvim")]
    public async Task Without_another_harness_the_sub_is_a_claude_sub_exactly_as_before(string harness)
    {
        var reply = await Handler().HandleAsync(Command(harness), "t");

        Assert.True(reply.Succeeded, reply.Error);
        var folder = reply.Value!.Folder;
        Assert.True(File.Exists(Path.Combine(folder, "CLAUDE.md")));
        Assert.False(File.Exists(Path.Combine(folder, "AGENTS.md")));
        Assert.Equal(string.Empty, Assert.Single(_store.Saved).OrchestratorHarness);
        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(
            AgentHarness.OrchestratorCommand(
                resume: false,
                inNvim: false,
                ClaudeLaunch.ForAgent("techweb", string.Empty, "do-the-thing", true, SettingsDefaults.Models)),
            _mux.ArgsFor(pane.Id));
        Assert.Equal(AgentHarness.SessionPersistence, _mux.EnvFor(pane.Id));
        Assert.Equal([AgentHarness.OrchestratorKickoff, "\r"], _mux.SentTo(pane.Id));
    }

    [Fact]
    public async Task Without_a_harness_argument_the_claude_config_writer_it_was_built_with_is_used()
    {
        var reply = await Handler().HandleAsync(Command(string.Empty), "t");

        Assert.Equal([("claude-default", reply.Value!.Folder, "techweb", "do-the-thing")], _claude.Written);
        Assert.Empty(_configs.Written);
    }

    [Fact]
    public async Task An_unregistered_harness_fails_before_touching_the_disk()
    {
        var reply = await Handler().HandleAsync(Command("gemini"), "t");

        Assert.False(reply.Succeeded);
        Assert.Contains("'gemini' is not a registered harness", reply.Error);
        Assert.Contains("nvim, claude, opencode, codex", reply.Error);
        Assert.Empty(_store.Saved);
        Assert.Empty(await _mux.ListPanesAsync());
        Assert.False(Directory.Exists(OrchestrationPaths.Root(_root)));
    }

    [Fact]
    public async Task A_config_that_could_not_be_written_is_said_in_the_reply()
    {
        _configs.Fails = "could not edit ~/.codex/config.toml";

        var reply = await Handler().HandleAsync(Command("codex"), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Contains("could not edit ~/.codex/config.toml", reply.Value!.Note);
        Assert.Contains("codex", reply.Value.Note);
    }

    [Fact]
    public async Task A_direct_repository_agent_is_started_with_the_harness()
    {
        var starter = new Starter();

        var reply = await Handler(starter: starter)
            .HandleAsync(Command("opencode") with { Repository = "api", Branch = "fix" }, "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Equal("opencode", Assert.Single(starter.Started).Harness);
        Assert.Empty(_store.Saved);
    }

    [Theory]
    [InlineData("nvim", "nvim")]
    [InlineData("pi", "pi")]
    [InlineData("", "")]
    public async Task A_direct_repository_agent_gets_the_harness_name_as_it_was_given(string given, string started)
    {
        var starter = new Starter();

        await Handler(starter: starter).HandleAsync(Command(given) with { Repository = "api", Branch = "fix" }, "t");

        Assert.Equal(started, Assert.Single(starter.Started).Harness);
    }

    private sealed class RecordingConfig(string name) : IHarnessConfig
    {
        public List<(string, string, string, string)> Written { get; } = [];

        public string? Fails { get; set; }

        public Result WriteForOrchestration(string folder, string project, string caller)
        {
            Written.Add((name, folder, project, caller));

            return Fails is { } reason ? Result.Fail(reason) : Result.Ok();
        }

        public Result WriteForAgent(string folder, string project, string repository, string branch) => Result.Ok();
    }

    private sealed class RecordingConfigs : IHarnessConfigs
    {
        public List<(string, string, string, string)> Written { get; } = [];

        public string? Fails { get; set; }

        public IHarnessConfig For(string harness) => new Named(this, harness);

        private sealed class Named(RecordingConfigs owner, string harness) : IHarnessConfig
        {
            public Result WriteForOrchestration(string folder, string project, string caller)
            {
                owner.Written.Add((harness, folder, project, caller));

                return owner.Fails is { } reason ? Result.Fail(reason) : Result.Ok();
            }

            public Result WriteForAgent(string folder, string project, string repository, string branch) =>
                Result.Ok();
        }
    }

    private sealed class Starter : IAgentStarter
    {
        public List<AgentStart> Started { get; } = [];

        public Task<IReadOnlyCollection<string>> BranchesAsync(
            string project, string repository, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<Result<string>> StartAsync(AgentStart request, CancellationToken ct = default)
        {
            Started.Add(request);

            return Task.FromResult(Result<string>.Ok("started."));
        }
    }

    private sealed class Settings(SettingsConfig config) : ISettingsStore
    {
        public SettingsConfig Load(string project) => config;

        public void Save(string project, SettingsConfig config)
        {
        }
    }

    private sealed class Store : IAgentStore
    {
        public List<AgentRecord> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Saved;

        public bool Replace(string project, AgentRecord agent)
        {
            Save(project, agent);
            return true;
        }

        public bool Update(string project, string worktree, Func<AgentRecord, AgentRecord> change) =>
            List(project).FirstOrDefault(a => a.Worktree == worktree) is { } current && Replace(project, change(current));

        public bool Remove(string project, string worktree) => Saved.RemoveAll(a => a.Worktree == worktree) > 0;
    }
}
