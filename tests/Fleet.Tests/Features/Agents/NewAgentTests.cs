using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Agents.TellAgent;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Orchestrations;
using Fleet.Ports.Settings;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Agents;

public sealed class NewAgentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly RecordingAgentStore _store = new();

    private readonly FakeMuxDriver _mux = new();

    public NewAgentTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);

                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private NewAgentHandler Handler() => new(new GitRunner(), _mux, _store);

    private async Task<string> RepositoryAsync(string name = "backend", string branch = "main")
    {
        var created = await new AddRepositoryHandler(new GitRunner())
            .HandleAsync(AddRepositoryCommand.CreateNew(_root, name, branch));

        Assert.True(created.Succeeded, created.Error);

        return created.Value!.Path;
    }

    private NewAgentCommand Command(string directory, string branch, string from = "") =>
        new("techweb", "backend", directory, branch, from, "claude");

    [Fact]
    public async Task An_agent_gets_a_worktree_of_its_own_beside_the_others()
    {
        var directory = await RepositoryAsync();

        var result = await Handler().HandleAsync(Command(directory, "feature/login"));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(Directory.Exists(Path.Combine(directory, "feature_login")));
        Assert.True(Directory.Exists(Path.Combine(directory, "main")));
    }

    [Fact]
    public async Task The_agent_is_recorded_against_its_worktree_path()
    {
        var directory = await RepositoryAsync();

        var result = await Handler().HandleAsync(Command(directory, "feature/login"));

        var saved = Assert.Single(_store.Saved);

        Assert.Equal("techweb", saved.Project);
        Assert.Equal(Path.Combine(directory, "feature_login"), saved.Agent.Worktree);
        Assert.Equal(result.Value!.Worktree, saved.Agent.Worktree);
        Assert.Equal("feature/login", saved.Agent.Branch);
        Assert.Equal("claude", saved.Agent.Harness);
    }

    [Fact]
    public async Task A_repo_agent_starts_with_the_projects_agent_model_and_effort()
    {
        var directory = await RepositoryAsync();
        var asked = new List<string>();
        var models = SettingsDefaults.Models with { Agent = new RoleModel("opus", "xhigh") };

        await new NewAgentHandler(new GitRunner(), _mux, _store, project => { asked.Add(project); return models; })
            .HandleAsync(Command(directory, "feat/x"));

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.Equal(
            [AgentHarness.Claude, "--name", "techweb-backend-feat-x", "--model", "opus", "--effort", "xhigh"],
            _mux.ArgsFor(pane.Id));
        Assert.Equal(["techweb"], asked);
    }

    [Fact]
    public async Task The_harness_is_spawned_in_the_worktree_not_the_repository()
    {
        var directory = await RepositoryAsync();

        await Handler().HandleAsync(Command(directory, "feature/login"));

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.Equal(Path.Combine(directory, "feature_login"), pane.Cwd);
        Assert.Equal([AgentHarness.Claude, "--name", "techweb-backend-feature-login"], _mux.ArgsFor(pane.Id));
    }

    [Fact]
    public async Task An_agent_the_main_orchestrator_starts_runs_claude_even_without_an_owner()
    {
        var directory = await RepositoryAsync();

        var result = await Handler().HandleAsync(
            new NewAgentCommand("techweb", "backend", directory, "feature/health", string.Empty, AgentHarness.Nvim, Owner: string.Empty, Claude: true));

        Assert.True(result.Succeeded, result.Error);
        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Contains(_mux.ArgsFor(pane.Id), arg => arg.Contains("ClaudeCode", StringComparison.Ordinal));
        Assert.True(Assert.Single(_store.Saved).Agent.Claude);
    }

    [Fact]
    public async Task A_manual_nvim_agent_stays_editor_only_and_says_so_in_its_record()
    {
        var directory = await RepositoryAsync();

        await Handler().HandleAsync(new NewAgentCommand("techweb", "backend", directory, "feature/notes", string.Empty, AgentHarness.Nvim));

        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.DoesNotContain(_mux.ArgsFor(pane.Id), arg => arg.Contains("ClaudeCode", StringComparison.Ordinal));
        Assert.False(Assert.Single(_store.Saved).Agent.Claude);
    }

    [Fact]
    public void A_record_from_before_the_flag_runs_claude_when_a_sub_orchestrator_owns_it()
    {
        var legacy = new AgentRecord("C:/w", "api", "b", AgentHarness.Nvim, "origin/main", false);

        Assert.False(legacy.RunsClaude);
        Assert.True((legacy with { Owner = "sub-1" }).RunsClaude);
        Assert.True((legacy with { Claude = true }).RunsClaude);
        Assert.False((legacy with { Owner = "sub-1", Claude = false }).RunsClaude);
    }

    [Fact]
    public async Task A_new_agent_forces_session_persistence_so_its_claude_saves_transcripts()
    {
        var directory = await RepositoryAsync();

        await Handler().HandleAsync(Command(directory, "feature/login"));

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.Equal("1", _mux.EnvFor(pane.Id)["CLAUDE_CODE_FORCE_SESSION_PERSISTENCE"]);
    }

    [Fact]
    public async Task The_pane_is_titled_with_the_repository_and_the_slugged_branch()
    {
        var directory = await RepositoryAsync();

        await Handler().HandleAsync(Command(directory, "feature/login"));

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.Equal("backend/feature_login", _mux.TitleOf(pane.Id));
    }

    [Fact]
    public async Task A_branch_that_already_has_a_worktree_is_reused_rather_than_failing()
    {
        var directory = await RepositoryAsync();

        var result = await Handler().HandleAsync(Command(directory, "main"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(Path.Combine(directory, "main"), result.Value!.Worktree);
        Assert.Single(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task An_empty_branch_name_is_refused_before_anything_is_created()
    {
        var directory = await RepositoryAsync();

        var result = await Handler().HandleAsync(Command(directory, "   "));

        Assert.False(result.Succeeded);
        Assert.Empty(_store.Saved);
        Assert.Empty(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task A_base_branch_that_does_not_exist_is_refused_with_its_name()
    {
        var directory = await RepositoryAsync();

        var result = await Handler().HandleAsync(Command(directory, "feature/x", from: "nope"));

        Assert.False(result.Succeeded);
        Assert.Contains("nope", result.Error);
        Assert.False(Directory.Exists(Path.Combine(directory, "feature_x")));
    }

    [Fact]
    public async Task An_agent_branch_cut_from_a_remote_base_gets_no_mismatched_upstream()
    {
        var directory = await RepositoryAsync();
        var git = new GitRunner();

        var remote = Path.Combine(_root, "origin.git");
        await git.RunAsync(_root, ["init", "--bare", remote]);
        await git.RunAsync(directory, ["remote", "add", "origin", remote]);
        await git.RunAsync(directory, ["push", "origin", "main"]);
        await git.RunAsync(directory, ["fetch", "origin"]);

        var result = await Handler()
            .HandleAsync(Command(directory, "feature/login", from: "origin/main"));

        Assert.True(result.Succeeded, result.Error);

        var upstream = await git.RunAsync(
            result.Value!.Worktree,
            ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"]);

        Assert.False(upstream.Ok);
    }

    [Fact]
    public async Task A_repository_whose_trunk_is_not_main_still_cuts_from_its_own_head()
    {
        var directory = await RepositoryAsync(branch: "master");

        var result = await Handler().HandleAsync(Command(directory, "feature/x"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("master", result.Value!.BaseRef);
    }

    [Fact]
    public async Task A_new_agent_starts_in_the_projects_hidden_workspace_and_a_task_still_reaches_it()
    {
        var directory = await RepositoryAsync();
        var mux = new FakeMuxDriver(workspaces: true);
        var visible = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Cwd = _root });
        await mux.FocusPaneAsync(visible);

        var result = await new NewAgentHandler(new GitRunner(), mux, _store)
            .HandleAsync(Command(directory, "feature/login"));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.Hidden);
        Assert.True(Assert.Single(_store.Saved).Agent.Hidden);

        var pane = Assert.Single(await mux.ListPanesAsync(), p => p.Id != visible);
        Assert.Equal(FleetWorkspaces.HiddenFor("techweb"), pane.SessionName);
        Assert.False(pane.IsActive);
        Assert.True(Assert.Single(await mux.ListPanesAsync(), p => p.Id == visible).IsActive);
        Assert.DoesNotContain(mux.Calls, c => c == "show" || c.StartsWith("open-window", StringComparison.Ordinal));

        await new TellAgentHandler(mux, TimeSpan.Zero).DeliverAsync(result.Value, pane.Id, "add a login page");

        Assert.Equal([AgentHarness.AgentInstructionPrompt, "\r"], mux.SentTo(pane.Id));
        Assert.Equal(
            $"add a login page\n\n{AgentHarness.AgentReportRule}",
            File.ReadAllText(Path.Combine(pane.Cwd, ".fleet", AgentHarness.AgentInstructionFile)));
    }

    [Fact]
    public async Task Without_workspaces_a_new_agent_starts_in_the_shared_hidden_workspace()
    {
        var directory = await RepositoryAsync();

        var result = await Handler().HandleAsync(Command(directory, "feature/login"));

        Assert.True(result.Value!.Hidden);
        Assert.Equal(FleetWorkspaces.Hidden, Assert.Single(await _mux.ListPanesAsync()).SessionName);
    }

    [Fact]
    public async Task An_agent_a_sub_orchestrator_starts_is_hidden_too_and_its_task_lands_in_the_inbox()
    {
        var directory = await RepositoryAsync();
        var mux = new FakeMuxDriver(workspaces: true);

        var result = await new NewAgentHandler(new GitRunner(), mux, _store).HandleAsync(
            new NewAgentCommand(
                "techweb", "backend", directory, "feature/health", string.Empty, AgentHarness.Nvim,
                Owner: "upgrade-node"));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.Hidden);
        Assert.Equal("upgrade-node", result.Value.Owner);

        var pane = Assert.Single(await mux.ListPanesAsync());
        Assert.True(FleetWorkspaces.IsHidden(pane.SessionName));

        await new TellAgentHandler(mux, TimeSpan.Zero).DeliverAsync(result.Value, pane.Id, "add a health check");

        Assert.Equal(
            $"add a health check\n\n{AgentHarness.AgentReportRule}",
            File.ReadAllText(Path.Combine(pane.Cwd, ".fleet", AgentHarness.AgentInstructionFile)));
    }

    [Fact]
    public async Task The_toggle_still_shows_an_agent_that_started_hidden()
    {
        var directory = await RepositoryAsync();
        var mux = new FakeMuxDriver(workspaces: true);

        var result = await new NewAgentHandler(new GitRunner(), mux, _store)
            .HandleAsync(Command(directory, "feature/login"));

        var shown = await new HideAgentHandler(mux, _store)
            .HandleAsync("techweb", result.Value!, dashboardWindow: "techweb");

        Assert.False(shown.Value!.Hidden);
        Assert.Equal("techweb", Assert.Single(await mux.ListPanesAsync()).SessionName);
    }

    private sealed record Saved(string Project, AgentRecord Agent);

    private sealed class RecordingAgentStore : IAgentStore
    {
        public List<Saved> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(new Saved(project, agent));

        public IReadOnlyList<AgentRecord> List(string project) =>
            Saved.Where(s => s.Project == project).Select(s => s.Agent).ToList();

        public bool Replace(string project, AgentRecord agent)
        {
            Save(project, agent);
            return true;
        }

        public bool Remove(string project, string worktree)
        {
            Saved.RemoveAll(s => s.Project == project && s.Agent.Worktree == worktree);
            return true;
        }
    }

    [Fact]
    public async Task With_subagent_guidance_on_a_repo_agent_launches_with_fleets_guidance_file()
    {
        var directory = await RepositoryAsync();
        var asked = new List<string>();

        await new NewAgentHandler(new GitRunner(), _mux, _store, subagentGuidance: project => { asked.Add(project); return true; })
            .HandleAsync(Command(directory, "feat/x"));

        var pane = Assert.Single(await _mux.ListPanesAsync());
        var worktree = Path.Combine(directory, "feat_x");

        Assert.Equal(
            [AgentHarness.Claude, "--name", "techweb-backend-feat-x", SubagentGuidance.AppendFlag, SubagentGuidance.RelativePath],
            _mux.ArgsFor(pane.Id));
        Assert.Equal(SubagentGuidance.ForRepoAgents, File.ReadAllText(Path.Combine(worktree, SubagentGuidance.RelativePath)));
        Assert.Equal(["techweb"], asked);
    }

    [Fact]
    public async Task With_subagent_guidance_off_a_repo_agent_gets_no_flag_and_no_file()
    {
        var directory = await RepositoryAsync();

        await new NewAgentHandler(new GitRunner(), _mux, _store, subagentGuidance: _ => false)
            .HandleAsync(Command(directory, "feat/x"));

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.DoesNotContain(SubagentGuidance.AppendFlag, _mux.ArgsFor(pane.Id));
        Assert.False(SubagentGuidance.IsIn(Path.Combine(directory, "feat_x")));
    }

    [Fact]
    public async Task With_subagent_guidance_off_an_existing_worktree_loses_its_old_guidance_file()
    {
        var directory = await RepositoryAsync();
        var worktree = Path.Combine(directory, "feat_x");

        await new NewAgentHandler(new GitRunner(), _mux, _store, subagentGuidance: _ => true)
            .HandleAsync(Command(directory, "feat/x"));
        Assert.True(SubagentGuidance.IsIn(worktree));

        await new NewAgentHandler(new GitRunner(), _mux, _store, subagentGuidance: _ => false)
            .HandleAsync(Command(directory, "feat/x"));

        Assert.False(SubagentGuidance.IsIn(worktree));
        Assert.DoesNotContain(SubagentGuidance.AppendFlag, _mux.ArgsFor((await _mux.ListPanesAsync())[^1].Id));
    }

    [Fact]
    public async Task A_directly_dispatched_agent_in_nvim_gets_the_guidance_through_claudecode()
    {
        var directory = await RepositoryAsync();

        await new NewAgentHandler(new GitRunner(), _mux, _store, subagentGuidance: _ => true).HandleAsync(
            new NewAgentCommand("techweb", "backend", directory, "feat/direct", "main", AgentHarness.Nvim, "main", Claude: true));

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.Contains(
            $"ClaudeCode --name techweb-backend-feat-direct {SubagentGuidance.AppendFlag} {SubagentGuidance.RelativePath}'",
            _mux.ArgsFor(pane.Id)[2],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_manual_nvim_agent_without_claude_gets_no_guidance_file()
    {
        var directory = await RepositoryAsync();

        await new NewAgentHandler(new GitRunner(), _mux, _store, subagentGuidance: _ => true)
            .HandleAsync(new NewAgentCommand("techweb", "backend", directory, "feature/notes", string.Empty, AgentHarness.Nvim));

        Assert.False(SubagentGuidance.IsIn(Path.Combine(directory, "feature_notes")));
    }
    private static readonly RoleModels AutoAgent =
        SettingsDefaults.Models with { Agent = new RoleModel(ModelChoice.Auto, ModelChoice.Inherit) };

    private readonly RecordingLog _log = new();

    private NewAgentHandler Routed(RoleModels models, IModelRouter? router) =>
        new(new GitRunner(), _mux, _store, _ => models, router: router, log: _log);

    private NewAgentCommand TaskCommand(string directory, string task) =>
        new("techweb", "backend", directory, "feat/x", string.Empty, "claude", Task: task);

    private async Task<IReadOnlyList<string>> AgentArgs() =>
        _mux.ArgsFor(Assert.Single(await _mux.ListPanesAsync()).Id);

    [Fact]
    public async Task With_the_agent_model_on_auto_the_agent_launches_with_the_routed_model_and_effort()
    {
        var directory = await RepositoryAsync();
        var router = new FakeModelRouter(new RoleModel("opus", "high"));

        var result = await Routed(AutoAgent, router).HandleAsync(TaskCommand(directory, "fix the login timeout"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            [AgentHarness.Claude, "--name", "techweb-backend-feat-x", "--model", "opus", "--effort", "high"],
            await AgentArgs());
        Assert.Equal(new ModelRoutingRequest("fix the login timeout", null, false, "backend"), Assert.Single(router.Requests));
        Assert.Empty(_log.Lines);
    }

    [Fact]
    public async Task A_routed_agent_keeps_its_model_and_effort_on_its_record()
    {
        var directory = await RepositoryAsync();

        var result = await Routed(AutoAgent, new FakeModelRouter(new RoleModel("opus", "high")))
            .HandleAsync(TaskCommand(directory, "fix the login timeout"));

        Assert.Equal(new RoleModel("opus", "high"), result.Value!.Model);
        Assert.Equal(new RoleModel("opus", "high"), _store.Saved.Last().Agent.Model);
    }

    [Fact]
    public async Task With_a_fixed_agent_model_the_router_is_never_asked_and_nothing_is_stored()
    {
        var directory = await RepositoryAsync();
        var router = new FakeModelRouter(new RoleModel("opus", "high"));

        var result = await Routed(SettingsDefaults.Models, router).HandleAsync(TaskCommand(directory, "fix it"));

        Assert.Empty(router.Requests);
        Assert.Null(result.Value!.Model);
        Assert.Equal([AgentHarness.Claude, "--name", "techweb-backend-feat-x"], await AgentArgs());
    }

    public static TheoryData<string> RouterFailures => ["nothing", "throws", "invalid", "no router"];

    [Theory]
    [MemberData(nameof(RouterFailures))]
    public async Task A_router_that_fails_launches_on_inherit_logs_it_stores_no_model_and_still_starts_the_agent(string failure)
    {
        var directory = await RepositoryAsync();
        var router = failure switch
        {
            "throws" => new FakeModelRouter(throws: true),
            "invalid" => new FakeModelRouter(new RoleModel("gpt-4", "extreme")),
            "no router" => null,
            _ => new FakeModelRouter(null),
        };

        var result = await Routed(AutoAgent, router).HandleAsync(TaskCommand(directory, "fix it"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal([AgentHarness.Claude, "--name", "techweb-backend-feat-x"], await AgentArgs());
        Assert.Null(result.Value!.Model);
        Assert.Null(_store.Saved.Last().Agent.Model);
        Assert.Contains(_log.Lines, l => l.Contains("backend/feat/x") && l.Contains("inherit"));
    }

    [Fact]
    public async Task An_auto_agent_without_a_task_is_not_routed_runs_on_inherit_and_stores_no_model()
    {
        var directory = await RepositoryAsync();
        var router = new FakeModelRouter(new RoleModel("opus", "high"));

        var result = await Routed(AutoAgent, router).HandleAsync(TaskCommand(directory, "  "));

        Assert.Empty(router.Requests);
        Assert.Equal([AgentHarness.Claude, "--name", "techweb-backend-feat-x"], await AgentArgs());
        Assert.Null(result.Value!.Model);
    }

    [Fact]
    public async Task Restarting_a_routed_agent_without_a_task_keeps_its_stored_model()
    {
        var directory = await RepositoryAsync();
        var first = await Routed(AutoAgent, new FakeModelRouter(new RoleModel("opus", "high")))
            .HandleAsync(TaskCommand(directory, "fix the login timeout"));
        var router = new FakeModelRouter(new RoleModel("haiku", "low"));

        var again = await Routed(AutoAgent, router).HandleAsync(TaskCommand(directory, string.Empty));

        Assert.Equal(first.Value!.Worktree, again.Value!.Worktree);
        Assert.Empty(router.Requests);
        Assert.Equal(new RoleModel("opus", "high"), again.Value!.Model);
        Assert.Equal(
            [AgentHarness.Claude, "--name", "techweb-backend-feat-x", "--model", "opus", "--effort", "high"],
            _mux.ArgsFor((await _mux.ListPanesAsync())[^1].Id));
    }

    [Fact]
    public async Task Restarting_a_routed_agent_whose_new_route_fails_keeps_its_stored_model()
    {
        var directory = await RepositoryAsync();
        await Routed(AutoAgent, new FakeModelRouter(new RoleModel("opus", "high")))
            .HandleAsync(TaskCommand(directory, "fix the login timeout"));

        var again = await Routed(AutoAgent, new FakeModelRouter(null)).HandleAsync(TaskCommand(directory, "now the logout"));

        Assert.Equal(new RoleModel("opus", "high"), again.Value!.Model);
    }

    [Fact]
    public async Task An_auto_agent_that_does_not_run_claude_is_not_routed()
    {
        var directory = await RepositoryAsync();
        var router = new FakeModelRouter(new RoleModel("opus", "high"));

        var result = await Routed(AutoAgent, router).HandleAsync(
            new NewAgentCommand("techweb", "backend", directory, "feat/x", string.Empty, AgentHarness.Nvim, Task: "fix it"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(router.Requests);
        Assert.Null(result.Value!.Model);
    }

    [Fact]
    public async Task A_created_agent_remembers_its_repository_and_harness()
    {
        var directory = await RepositoryAsync();
        var settings = new FakeSettings();

        var result = await new NewAgentHandler(new GitRunner(), _mux, _store, settings: settings)
            .HandleAsync(Command(directory, "feature/x"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("techweb", settings.SavedFor);
        Assert.Equal(("backend", "claude"), (settings.Stored.LastRepo, settings.Stored.LastHarness));
    }

    [Fact]
    public async Task Remembering_keeps_the_other_settings()
    {
        var directory = await RepositoryAsync();
        var settings = new FakeSettings(SettingsConfig.Default.WithTrigger(";"));

        await new NewAgentHandler(new GitRunner(), _mux, _store, settings: settings)
            .HandleAsync(Command(directory, "feature/x"));

        Assert.Equal(";", settings.Stored.Trigger);
    }

    [Fact]
    public async Task A_refused_agent_remembers_nothing()
    {
        var directory = await RepositoryAsync();
        var settings = new FakeSettings();

        var result = await new NewAgentHandler(new GitRunner(), _mux, _store, settings: settings)
            .HandleAsync(Command(directory, "feature/x", from: "nope"));

        Assert.False(result.Succeeded);
        Assert.Null(settings.SavedFor);
    }

    private sealed class FakeSettings(SettingsConfig? stored = null) : ISettingsStore
    {
        public SettingsConfig Stored { get; private set; } = stored ?? SettingsConfig.Default;

        public string? SavedFor { get; private set; }

        public SettingsConfig Load(string project) => Stored;

        public void Save(string project, SettingsConfig config)
        {
            SavedFor = project;
            Stored = config;
        }
    }

    private sealed class FakeModelRouter(RoleModel? choice = null, bool throws = false) : IModelRouter
    {
        public List<ModelRoutingRequest> Requests { get; } = [];

        public Task<RoleModel?> ChooseAsync(ModelRoutingRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            return throws
                ? throw new InvalidOperationException("claude is unreachable")
                : Task.FromResult(choice);
        }
    }

    private sealed class RecordingLog : IFleetLog
    {
        public List<string> Lines { get; } = [];

        public void Swallowed(Exception e) => Lines.Add(e.Message);

        public void Write(string line) => Lines.Add(line);

        public IReadOnlyList<string> Tail(int lines) => Lines.TakeLast(lines).ToList();
    }
}
