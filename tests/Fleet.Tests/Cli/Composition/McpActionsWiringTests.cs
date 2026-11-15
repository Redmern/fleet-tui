using System.Globalization;
using Fleet.Ports.Mux;
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

    private const string AgentInstructionPrompt = Fleet.Shared.Constants.AgentHarness.AgentInstructionPrompt;

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
