using System.Globalization;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings.Enums;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.Cli.Composition;

// Serial, because these tests point HOME, the XDG dirs and FLEET_CONFIG_HOME of the whole test process at a temp
// folder: nothing else may run while they are swapped.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessHomeCollection
{
    public const string Name = "process-home (swaps HOME for the whole process)";
}

// AC-13: dispatch, new_agent and open_agent through the real McpActions, with fakes for its seams (harness config
// writers, slug namer, clock, folder-trust writer) and a temp HOME. Each test ends by checking that nothing was
// written to the temp HOME (where the real writers would now land) and that the real home was left alone.
[Collection(ProcessHomeCollection.Name)]
public sealed class McpActionsWiringTests : IDisposable
{
    private static readonly string[] Swapped =
    [
        "HOME", "USERPROFILE", "CLAUDE_CONFIG_DIR", "CODEX_HOME", "PI_CODING_AGENT_DIR", "XDG_CONFIG_HOME",
        "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME", "XDG_RUNTIME_DIR", "FLEET_ENDPOINT", "FLEET_CONFIG_HOME",
    ];

    private readonly Dictionary<string, string?> _environment = Swapped.ToDictionary(n => n, Environment.GetEnvironmentVariable);
    private readonly Dictionary<string, bool> _realFilesBefore = RealHarnessFiles().ToDictionary(f => f, File.Exists);
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
    private readonly string _home;
    private readonly MixedTeamRig _team;

    public McpActionsWiringTests()
    {
        _home = Path.Combine(_sandbox, "home");
        Directory.CreateDirectory(_home);

        Environment.SetEnvironmentVariable("HOME", _home);
        Environment.SetEnvironmentVariable("USERPROFILE", _home);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("CODEX_HOME", null);
        Environment.SetEnvironmentVariable("PI_CODING_AGENT_DIR", null);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_home, ".config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_home, ".local", "share"));
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", Path.Combine(_home, ".local", "state"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(_home, ".cache"));
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", Path.Combine(_sandbox, "run"));
        Environment.SetEnvironmentVariable("FLEET_ENDPOINT", Path.Combine(_sandbox, "run", "fleet.sock"));
        Environment.SetEnvironmentVariable("FLEET_CONFIG_HOME", Path.Combine(_sandbox, "config"));

        try
        {
            _team = new MixedTeamRig(Path.Combine(_sandbox, "project"));
        }
        catch
        {
            // xunit does not dispose a class whose constructor threw
            RestoreEnvironment();
            throw;
        }
    }

    private void RestoreEnvironment()
    {
        foreach (var (name, value) in _environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        RestoreEnvironment();

        try
        {
            Directory.Delete(_sandbox, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Dispatch_names_the_sub_with_the_injected_namer_and_stamps_it_with_the_injected_clock()
    {
        _team.Namer.Answer = "login-fix";
        _team.Clock.Advance(TimeSpan.FromDays(4000));
        var stamp = _team.Clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        var dispatched = await _team.DispatchAsync(string.Empty, "fix the login bug", string.Empty);

        Assert.Equal(["fix the login bug"], _team.Namer.Prompts);
        Assert.Equal("login-fix", dispatched.Slug);
        Assert.Contains($"Dispatched by fleet on {stamp}", File.ReadAllText(OrchestrationPaths.TaskFile(dispatched.Folder)));
        Assert.Equal(
            [new ConfigWrite(HarnessRegistry.ClaudeName, "orchestration", dispatched.Folder, _team.Project, "login-fix")],
            _team.ClaudeConfigs.Writes);
        Assert.Empty(_team.OtherConfigs.Writes);
        Assert.Equal(["claude"], _team.Mux.ArgsOf(_team.Mux.HarnessIn(dispatched.Folder)).Take(1));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task Dispatch_on_another_harness_writes_its_config_through_the_injected_writers()
    {
        var dispatched = await _team.DispatchAsync(string.Empty, "fix the login bug", HarnessRegistry.CodexName);

        Assert.Equal(
            [new ConfigWrite(HarnessRegistry.CodexName, "orchestration", dispatched.Folder, _team.Project, dispatched.Slug)],
            _team.OtherConfigs.Writes);
        Assert.Empty(_team.ClaudeConfigs.Writes);
        Assert.Contains(dispatched.Folder, File.ReadAllText(_team.CodexUserConfig));
        Assert.Equal(dispatched.Slug, _team.Mux.HarnessIn(dispatched.Folder).Caller);
        Assert.Equal(1, _team.Mux.HarnessIn(dispatched.Folder).LostTypings);

        // the kickoff was lost once and typed again, and every echo poll of that retry was taken on the injected clock
        Assert.Equal(EchoPollsOfOneLostTyping, _team.Clock.Delays.Count(d => d == TypedPrompt.EchoDelay));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task New_agent_writes_its_configs_through_the_injected_writers_and_waits_on_the_injected_clock()
    {
        await _team.AddRepositoryAsync("api");

        var started = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat"), ("harness", "opencode"), ("task", "build the form"));

        Assert.False(started.IsError, started.Text);
        Assert.Contains("started api/feat and gave it its first task.", started.Text);

        var feat = _team.Record("api", "feat");

        Assert.Equal(
            [new ConfigWrite(HarnessRegistry.OpencodeName, "agent", feat.Worktree, _team.Project, "api/feat")],
            _team.OtherConfigs.Writes);
        Assert.Equal(
            [new ConfigWrite(HarnessRegistry.OpencodeName, "agent", feat.Worktree, _team.Project, "api/feat")],
            _team.ClaudeConfigs.Writes);
        Assert.Equal([AgentInstructionPrompt], _team.Mux.HarnessIn(feat.Worktree).Submitted);
        Assert.Equal(1, _team.Mux.HarnessIn(feat.Worktree).LostTypings);

        // the first task was lost once and typed again, and every echo poll of that retry was taken on the injected
        // clock
        Assert.Equal(EchoPollsOfOneLostTyping, _team.Clock.Delays.Count(d => d == TypedPrompt.EchoDelay));

        var claude = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent, ("repository", "api"), ("branch", "ui"), ("harness", "claude"));

        Assert.False(claude.IsError, claude.Text);
        Assert.Contains(
            new ConfigWrite(HarnessRegistry.ClaudeName, "agent", _team.Record("api", "ui").Worktree, _team.Project, "api/ui"),
            _team.ClaudeConfigs.Writes);

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task New_agent_says_the_first_task_is_written_but_not_confirmed_when_the_pane_never_shows_the_prompt()
    {
        await _team.AddRepositoryAsync("api");
        _team.Mux.HarnessesLoseEveryTyping = true;

        var started = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat"), ("harness", "opencode"), ("task", "build the form"));

        Assert.False(started.IsError, started.Text);
        Assert.DoesNotContain("gave it its first task", started.Text);
        Assert.Contains("started api/feat; its first task is written to .fleet/instruction.md", started.Text);
        Assert.Contains("delivery was not confirmed (its pane never showed the typed prompt", started.Text);

        var feat = _team.Record("api", "feat");

        Assert.Equal("build the form\n\n" + AgentHarness.AgentReportRule, _team.Mux.HarnessIn(feat.Worktree).Instruction);
        Assert.Empty(_team.Mux.HarnessIn(feat.Worktree).Submitted);
        Assert.Equal(2, _team.Mux.HarnessIn(feat.Worktree).LostTypings);

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task Dispatch_straight_to_a_repository_agent_says_the_first_task_was_not_confirmed_too()
    {
        await _team.AddRepositoryAsync("api");
        _team.Mux.HarnessesLoseEveryTyping = true;

        var reply = await _team.CallAsync(
            string.Empty, HarnessTool.Dispatch,
            ("message", "build the form"), ("repository", "api"), ("branch", "feat"), ("harness", "opencode"));

        Assert.False(reply.IsError, reply.Text);
        Assert.Contains("started a repository agent on api/feat", reply.Text);
        Assert.DoesNotContain("gave it its first task", reply.Text);
        Assert.Contains("delivery was not confirmed", reply.Text);

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task New_agent_on_claude_with_a_ready_marker_gives_the_first_task_without_a_doubt()
    {
        await _team.AddRepositoryAsync("api");

        var started = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat"), ("harness", "claude"), ("task", "build the form"));

        Assert.False(started.IsError, started.Text);
        Assert.Contains("started api/feat and gave it its first task.", started.Text);
        Assert.DoesNotContain("not confirmed", started.Text);
        Assert.Equal([AgentInstructionPrompt, "\r"], _team.Mux.TypedInto(_team.Mux.HarnessIn(_team.Record("api", "feat").Worktree)));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task New_agent_on_an_agent_hosted_in_nvim_gives_the_first_task_once_the_pump_marked_it_seen()
    {
        await _team.AddRepositoryAsync("api");
        _team.Clock.Skipped += () => PumpActs(seen: true);

        var started = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat"), ("harness", "nvim"), ("task", "build the form"));

        Assert.False(started.IsError, started.Text);
        Assert.Contains("started api/feat and gave it its first task.", started.Text);

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task New_agent_on_an_agent_hosted_in_nvim_says_not_confirmed_when_the_pump_never_marks_it_seen()
    {
        await _team.AddRepositoryAsync("api");
        _team.Clock.Skipped += () => PumpActs(seen: false);

        var started = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat"), ("harness", "nvim"), ("task", "build the form"));

        Assert.False(started.IsError, started.Text);
        Assert.DoesNotContain("gave it its first task", started.Text);
        Assert.Contains(
            "delivery was not confirmed (fleet stopped waiting for nvim to report it; nvim delivers it once the agent is ready)",
            started.Text);

        NothingLeftTheSandbox();
    }

    // One deadline per call: the caller's client tool limit less ten seconds, covering the ready wait, the typing and
    // the pump wait together. A codex caller (60 s) is answered inside 50 s, a claude caller (600 s) keeps the full wait.
    [Fact]
    public async Task New_agent_stops_waiting_for_the_pump_inside_the_codex_callers_tool_limit_and_not_for_claude()
    {
        await _team.AddRepositoryAsync("api");
        var lead = (await _team.DispatchAsync(string.Empty, "lead the work", "codex")).Slug;
        _team.Clock.Skipped += () => PumpActs(seen: false);

        var before = _team.Clock.GetUtcNow();
        var fromCodex = await _team.CallAsync(
            lead, HarnessTool.NewAgent, ("repository", "api"), ("branch", "a"), ("harness", "nvim"), ("task", "build a"));
        var codexWaited = _team.Clock.GetUtcNow() - before;

        before = _team.Clock.GetUtcNow();
        var fromClaude = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent, ("repository", "api"), ("branch", "b"), ("harness", "nvim"), ("task", "build b"));
        var claudeWaited = _team.Clock.GetUtcNow() - before;

        Assert.Contains("fleet stopped waiting for nvim to report it", fromCodex.Text);
        Assert.Contains("fleet stopped waiting for nvim to report it", fromClaude.Text);
        Assert.InRange(codexWaited, TimeSpan.FromSeconds(49), TimeSpan.FromSeconds(50.5));
        Assert.InRange(claudeWaited, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(62));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task Dispatch_to_an_nvim_sub_stops_waiting_for_the_pump_inside_the_codex_callers_tool_limit_and_not_for_claude()
    {
        _team.SubOrchestratorsInNvim(true);
        var lead = (await _team.DispatchAsync(string.Empty, "lead the work", "codex")).Slug;

        var before = _team.Clock.GetUtcNow();
        var fromCodex = await _team.CallAsync(lead, HarnessTool.Dispatch, ("message", "fix the login bug"));
        var codexWaited = _team.Clock.GetUtcNow() - before;

        before = _team.Clock.GetUtcNow();
        var fromClaude = await _team.CallAsync(string.Empty, HarnessTool.Dispatch, ("message", "fix the signup bug"));
        var claudeWaited = _team.Clock.GetUtcNow() - before;

        Assert.False(fromCodex.IsError, fromCodex.Text);
        Assert.Contains("delivery was not confirmed (fleet stopped waiting for nvim to report it", fromCodex.Text);
        Assert.Contains("delivery was not confirmed (fleet stopped waiting for nvim to report it", fromClaude.Text);
        Assert.InRange(codexWaited, TimeSpan.FromSeconds(49), TimeSpan.FromSeconds(50.5));
        Assert.InRange(claudeWaited, TimeSpan.FromSeconds(59), TimeSpan.FromSeconds(62));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task A_new_agent_waiting_on_an_approval_answers_inside_a_sixty_second_callers_tool_limit_counted_from_its_arrival()
    {
        await _team.AddRepositoryAsync("api");
        _team.Clock.Skipped += () => PumpActs(seen: false);

        var arrived = _team.Clock.GetUtcNow();
        var started = await _team.CallAskedAsync(
            "agent:api/ghost", HarnessTool.NewAgent, TimeSpan.FromSeconds(40),
            ("repository", "api"), ("branch", "feat"), ("harness", "nvim"), ("task", "build the form"));
        var answered = _team.Clock.GetUtcNow() - arrived;

        Assert.False(started.IsError, started.Text);
        Assert.Contains("delivery was not confirmed", started.Text);
        Assert.InRange(answered, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(50.5));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task A_dispatch_waiting_on_an_approval_answers_inside_the_codex_callers_tool_limit_counted_from_its_arrival()
    {
        _team.SubOrchestratorsInNvim(true);
        var lead = (await _team.DispatchAsync(string.Empty, "lead the work", "codex")).Slug;
        _team.Clock.Skipped += () => PumpActs(seen: false);

        var arrived = _team.Clock.GetUtcNow();
        var sent = await _team.CallAskedAsync(
            lead, HarnessTool.Dispatch, TimeSpan.FromSeconds(40), ("message", "fix the login bug"));
        var answered = _team.Clock.GetUtcNow() - arrived;

        Assert.False(sent.IsError, sent.Text);
        Assert.Contains("delivery was not confirmed", sent.Text);
        Assert.InRange(answered, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(50.5));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task A_dispatch_whose_approval_used_up_the_whole_budget_still_answers_not_confirmed_without_waiting_more()
    {
        _team.SubOrchestratorsInNvim(true);
        var lead = (await _team.DispatchAsync(string.Empty, "lead the work", "codex")).Slug;
        _team.Clock.Skipped += () => PumpActs(seen: false);

        var arrived = _team.Clock.GetUtcNow();
        var sent = await _team.CallAskedAsync(
            lead, HarnessTool.Dispatch, TimeSpan.FromSeconds(52), ("message", "fix the login bug"));
        var answered = _team.Clock.GetUtcNow() - arrived;

        Assert.False(sent.IsError, sent.Text);
        Assert.Contains("delivery was not confirmed", sent.Text);
        Assert.InRange(answered, TimeSpan.FromSeconds(52), TimeSpan.FromSeconds(52.5));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task A_dispatch_whose_caller_gave_up_stops_where_it_is_and_starts_nothing_more()
    {
        _team.SubOrchestratorsInNvim(true);
        using var gaveUp = new CancellationTokenSource();
        var polls = 0;
        _team.Clock.Skipped += () =>
        {
            if (++polls == 20)
            {
                gaveUp.Cancel();
            }
        };

        var request = new McpRequest(
            HarnessToolIds.For(HarnessTool.Dispatch), new Dictionary<string, string> { ["message"] = "fix the login bug" });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _team.Actions(string.Empty).PerformAsync(request, gaveUp.Token));

        var panes = await _team.Mux.ListPanesAsync();
        var delays = _team.Clock.Delays.Count;
        _team.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Single(_team.Store.List(_team.Project));
        Assert.Single(panes);
        Assert.Equal(delays, _team.Clock.Delays.Count);

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task New_agent_on_an_agent_hosted_in_nvim_says_what_nvim_reported_when_the_pump_could_not_see_the_prompt_arrive()
    {
        await _team.AddRepositoryAsync("api");
        _team.Clock.Skipped += () => PumpActs(seen: false, unconfirmed: true);

        var started = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat"), ("harness", "nvim"), ("task", "build the form"));

        Assert.False(started.IsError, started.Text);
        Assert.DoesNotContain("gave it its first task", started.Text);
        Assert.Contains(
            "delivery was not confirmed (nvim typed it twice, could not see it arrive, cleared the line and left it unsubmitted; send it again with tell_agent)",
            started.Text);

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task A_direct_dispatch_hands_its_one_deadline_to_the_agent_it_starts()
    {
        await _team.AddRepositoryAsync("api");
        _team.Clock.Skipped += () => PumpActs(seen: false);
        var by = _team.Clock.GetUtcNow() + TimeSpan.FromSeconds(20);

        var before = _team.Clock.GetUtcNow();
        var started = await _team.Actions(string.Empty).StartAsync(
            new AgentStart(_team.Project, "api", "feat", "build the form", "nvim", by));

        Assert.True(started.Succeeded, started.Error);
        Assert.InRange(_team.Clock.GetUtcNow() - before, TimeSpan.FromSeconds(19), TimeSpan.FromSeconds(21));
        Assert.Contains("fleet stopped waiting for nvim to report it", started.Value);

        NothingLeftTheSandbox();
    }

    // The ready marker claude's MCP server writes, and the seen marker nvim's pump leaves for the instruction file.
    private void PumpActs(bool seen, bool unconfirmed = false)
    {
        foreach (var agent in _team.Store.List(_team.Project))
        {
            var marker = OrchestrationPaths.ReadyMarker(agent.Worktree);
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, string.Empty);

            var instruction = Path.Combine(agent.Worktree, ".fleet", "instruction.md");

            if ((seen || unconfirmed) && File.Exists(instruction))
            {
                var written = new DateTimeOffset(File.GetLastWriteTimeUtc(instruction)).ToUnixTimeSeconds();
                File.WriteAllText(
                    Path.Combine(agent.Worktree, ".fleet", unconfirmed ? "instruction.unconfirmed" : "instruction.seen"),
                    written.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    [Fact]
    public async Task Open_agent_trusts_the_folder_through_the_injected_writer()
    {
        await _team.AddRepositoryAsync("api");
        await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent, ("repository", "api"), ("branch", "feat"), ("harness", "opencode"));

        var opened = await _team.CallAsync(string.Empty, HarnessTool.OpenAgent, ("repository", "api"), ("branch", "feat"));

        Assert.False(opened.IsError, opened.Text);
        Assert.Equal([_team.Record("api", "feat").Worktree], _team.TrustedFolders);

        NothingLeftTheSandbox();
    }

    // TypedPrompt polls for the echo every EchoDelay (400 ms) until the retry period (1 s) is over: three polls for
    // the typing the TUI lost, then one that sees the second typing. No other wait on these paths is 400 ms long (the
    // ready-marker polls are 200 ms and 300 ms), so these four are the retry itself.
    private const int EchoPollsOfOneLostTyping = 4;

    private const string AgentInstructionPrompt = AgentHarness.AgentInstructionPrompt;

    // What the real writers edit on this machine: claude's folder trust, codex's project trust and pi's trust store,
    // each in the home folder and in the folder its own variable names.
    private static IEnumerable<string> RealHarnessFiles()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return Path.Combine(home, ".claude.json");
        yield return Path.Combine(home, ".codex", "config.toml");
        yield return Path.Combine(home, ".pi", "agent", "trust.json");

        if (Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } claude)
        {
            yield return Path.Combine(claude, ".claude.json");
        }

        if (Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } codex)
        {
            yield return Path.Combine(codex, "config.toml");
        }

        if (Environment.GetEnvironmentVariable("PI_CODING_AGENT_DIR") is { Length: > 0 } pi)
        {
            yield return Path.Combine(pi, "trust.json");
        }
    }

    // On Windows the profile folder does not follow USERPROFILE, so there the temp HOME stays empty whatever fleet
    // does and only the check of the real files counts.
    // The real home may be in use by live harness sessions, so sizes and write times prove nothing there. Every
    // folder a test makes has the sandbox's random name in its path: a real file that names it was written by fleet.
    private async Task<(string Container, string Seed)> RepositoryWithOriginAsync(string name)
    {
        var container = await _team.AddRepositoryAsync(name);
        var git = new Fleet.Platform.Git.GitRunner();
        var upstream = Path.Combine(_sandbox, name + "-upstream.git");
        var seed = Path.Combine(_sandbox, name + "-seed");

        Assert.True((await git.RunAsync(_sandbox, ["init", "--bare", "-b", "main", upstream])).Ok);
        Assert.True((await git.RunAsync(container, ["remote", "add", "origin", upstream])).Ok);
        Assert.True((await git.RunAsync(container, ["config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*"])).Ok);
        Assert.True((await git.RunAsync(container, ["push", "origin", "main"])).Ok);
        Assert.True((await git.RunAsync(_sandbox, ["clone", upstream, seed])).Ok);

        return (container, seed);
    }

    private static async Task CommitUpstreamAsync(string seed, int count)
    {
        var git = new Fleet.Platform.Git.GitRunner();

        for (var i = 0; i < count; i++)
        {
            var committed = await git.RunAsync(
                seed, ["-c", "user.name=t", "-c", "user.email=t@t", "commit", "--allow-empty", "-m", $"upstream {i}"]);

            Assert.True(committed.Ok, committed.Message);
        }

        Assert.True((await git.RunAsync(seed, ["push", "origin", "HEAD:main"])).Ok);
    }

    [Fact]
    public async Task Pull_repository_without_a_default_worktree_moves_the_stale_local_default_and_repository_status_shows_the_lag()
    {
        var (container, seed) = await RepositoryWithOriginAsync("api");
        var git = new Fleet.Platform.Git.GitRunner();
        Assert.True((await git.RunAsync(container, ["worktree", "remove", "--force", Path.Combine(container, "main")])).Ok);
        await CommitUpstreamAsync(seed, 2);
        Assert.True((await git.RunAsync(container, ["fetch", "origin"])).Ok);

        var before = await _team.CallAsync(string.Empty, HarnessTool.RepositoryStatus, ("repository", "api"));

        Assert.False(before.IsError, before.Text);
        Assert.Contains("local main is 2 behind and 0 ahead of origin/main", before.Text);

        var pulled = await _team.CallAsync(string.Empty, HarnessTool.PullRepository, ("repository", "api"));

        Assert.False(pulled.IsError, pulled.Text);
        Assert.Contains("moved main", pulled.Text);
        Assert.Contains("2 commits", pulled.Text);

        var after = await _team.CallAsync(string.Empty, HarnessTool.RepositoryStatus, ("repository", "api"));

        Assert.Contains("local main is level with origin/main", after.Text);

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task Agent_status_names_the_base_ref_and_counts_the_commits_against_it_not_against_the_upstream()
    {
        var (container, _) = await RepositoryWithOriginAsync("api");
        var git = new Fleet.Platform.Git.GitRunner();
        var agent = await _team.StartClaudeWorkerAsync(string.Empty, "api", container, "feat/a");

        for (var i = 0; i < 2; i++)
        {
            var committed = await git.RunAsync(
                agent.Worktree,
                ["-c", "user.name=t", "-c", "user.email=t@t", "commit", "--allow-empty", "-m", $"work {i}"]);

            Assert.True(committed.Ok, committed.Message);
        }

        Assert.True((await git.RunAsync(agent.Worktree, ["push", "-u", "origin", "feat/a"])).Ok);

        var status = await _team.CallAsync(string.Empty, HarnessTool.AgentStatus, ("repository", "api"), ("branch", "feat/a"));

        Assert.False(status.IsError, status.Text);
        Assert.Contains($"base {agent.BaseRef}: 2 ahead", status.Text);
        Assert.DoesNotContain("in sync", status.Text);

        NothingLeftTheSandbox();
    }

    // A branch one commit ahead of main, made without a worktree. Returns its SHA.
    private static async Task<string> BranchAheadOfMainAsync(string container, string branch)
    {
        var git = new Fleet.Platform.Git.GitRunner();
        var commit = await git.RunAsync(
            container,
            ["-c", "user.name=t", "-c", "user.email=t@t", "commit-tree", "main^{tree}", "-p", "main", "-m", $"work on {branch}"]);

        Assert.True(commit.Ok, commit.Message);
        Assert.True((await git.RunAsync(container, ["branch", "--no-track", branch, commit.Out.Trim()])).Ok);

        return commit.Out.Trim();
    }

    private static async Task<string> TipAsync(string container, string reference)
    {
        var tip = await new Fleet.Platform.Git.GitRunner().RunAsync(container, ["rev-parse", reference]);

        Assert.True(tip.Ok, tip.Message);

        return tip.Out.Trim();
    }

    [Fact]
    public async Task New_agent_with_a_base_cuts_the_new_branch_from_that_base_and_records_it()
    {
        var container = await _team.AddRepositoryAsync("api");
        var parent = await BranchAheadOfMainAsync(container, "feat/parent");

        var reply = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat/child"), ("base", "feat/parent"), ("harness", "opencode"));

        Assert.False(reply.IsError, reply.Text);
        Assert.Contains("started api/feat/child", reply.Text);
        Assert.DoesNotContain("ignored", reply.Text);
        Assert.Equal(parent, await TipAsync(container, "feat/child"));
        Assert.Equal("feat/parent", _team.Record("api", "feat/child").BaseRef);

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task New_agent_with_a_base_for_a_branch_that_exists_says_the_base_was_ignored_and_names_the_tip()
    {
        var container = await _team.AddRepositoryAsync("api");
        await BranchAheadOfMainAsync(container, "feat/parent");
        var existing = await BranchAheadOfMainAsync(container, "feat/child");

        var reply = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat/child"), ("base", "feat/parent"), ("harness", "opencode"));

        Assert.False(reply.IsError, reply.Text);
        Assert.Contains($"base 'feat/parent' was ignored: feat/child already exists at {existing[..7]}", reply.Text);
        Assert.Equal(existing, await TipAsync(container, "feat/child"));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task New_agent_with_a_base_that_does_not_resolve_is_an_error_naming_it_and_starts_nothing()
    {
        var container = await _team.AddRepositoryAsync("api");

        var reply = await _team.CallAsync(
            string.Empty, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat/child"), ("base", "feat/nowhere"), ("harness", "opencode"));

        Assert.True(reply.IsError, reply.Text);
        Assert.Contains("base 'feat/nowhere'", reply.Text);
        Assert.False(Directory.Exists(Path.Combine(container, "feat_child")));

        NothingLeftTheSandbox();
    }

    [Fact]
    public async Task Dispatch_straight_to_a_repository_agent_with_a_base_cuts_the_new_branch_from_that_base()
    {
        var container = await _team.AddRepositoryAsync("api");
        var parent = await BranchAheadOfMainAsync(container, "feat/parent");

        var reply = await _team.CallAsync(
            string.Empty, HarnessTool.Dispatch,
            ("message", "build the form"), ("repository", "api"), ("branch", "feat/child"), ("base", "feat/parent"),
            ("harness", "opencode"));

        Assert.False(reply.IsError, reply.Text);
        Assert.Contains("started a repository agent on api/feat/child", reply.Text);
        Assert.Equal(parent, await TipAsync(container, "feat/child"));

        NothingLeftTheSandbox();
    }

    private void NothingLeftTheSandbox()
    {
        Assert.Empty(Directory.EnumerateFiles(_home, "*", SearchOption.AllDirectories));

        foreach (var (file, existed) in _realFilesBefore)
        {
            Assert.Equal(existed, File.Exists(file));

            if (existed)
            {
                Assert.DoesNotContain(Path.GetFileName(_sandbox), File.ReadAllText(file));
            }
        }
    }
}
