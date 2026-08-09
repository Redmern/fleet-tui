using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;

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
        Assert.Equal(["claude"], _mux.ArgsFor(pane.Id));
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
    public async Task A_repository_whose_trunk_is_not_main_still_cuts_from_its_own_head()
    {
        var directory = await RepositoryAsync(branch: "master");

        var result = await Handler().HandleAsync(Command(directory, "feature/x"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("master", result.Value!.BaseRef);
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
