using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Agents.TellAgent;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

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
    public async Task The_harness_is_spawned_in_the_worktree_not_the_repository()
    {
        var directory = await RepositoryAsync();

        await Handler().HandleAsync(Command(directory, "feature/login"));

        var pane = Assert.Single(await _mux.ListPanesAsync());

        Assert.Equal(Path.Combine(directory, "feature_login"), pane.Cwd);
        Assert.Equal([AgentHarness.Claude], _mux.ArgsFor(pane.Id));
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
            "add a login page",
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
            "add a health check",
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

        public void Remove(string project, string worktree) =>
            Saved.RemoveAll(s => s.Project == project && s.Agent.Worktree == worktree);
    }
}
