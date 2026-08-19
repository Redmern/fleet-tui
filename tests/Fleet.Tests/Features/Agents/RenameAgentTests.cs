using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Features.Agents.RenameAgent;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;

namespace Fleet.Tests.Features.Agents;

public sealed class RenameAgentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly MemoryStore _store = new();

    private readonly FakeMuxDriver _mux = new();

    public RenameAgentTests() => Directory.CreateDirectory(_root);

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

    private async Task<string> RepositoryAsync()
    {
        var created = await new AddRepositoryHandler(new GitRunner())
            .HandleAsync(AddRepositoryCommand.CreateNew(_root, "backend", "main"));

        Assert.True(created.Succeeded, created.Error);

        return created.Value!.Path;
    }

    private async Task<AgentRecord> AgentAsync(string repo, string branch)
    {
        var made = await new NewAgentHandler(new GitRunner(), _mux, _store)
            .HandleAsync(new NewAgentCommand("proj", "backend", repo, branch, string.Empty, "claude"));

        Assert.True(made.Succeeded, made.Error);

        return made.Value!;
    }

    [Fact]
    public async Task Renaming_moves_the_branch_the_worktree_and_the_record()
    {
        var repo = await RepositoryAsync();
        var agent = await AgentAsync(repo, "feature/old");

        var result = await new RenameAgentHandler(new GitRunner(), _store)
            .HandleAsync("proj", agent, "feature/new");

        Assert.True(result.Succeeded, result.Error);
        Assert.True(Directory.Exists(Path.Combine(repo, "feature_new")));
        Assert.False(Directory.Exists(Path.Combine(repo, "feature_old")));

        var saved = Assert.Single(_store.List("proj"));
        Assert.Equal("feature/new", saved.Branch);
        Assert.Equal(Path.Combine(repo, "feature_new"), saved.Worktree);

        var branch = await new GitRunner().RunAsync(repo, ["branch", "--list", "feature/new"]);
        Assert.Contains("feature/new", branch.Out);
    }

    [Fact]
    public async Task Renaming_onto_an_existing_branch_is_refused_and_changes_nothing()
    {
        var repo = await RepositoryAsync();
        var agent = await AgentAsync(repo, "feature/a");
        await AgentAsync(repo, "feature/b");

        var result = await new RenameAgentHandler(new GitRunner(), _store)
            .HandleAsync("proj", agent, "feature/b");

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Error);
        Assert.True(Directory.Exists(Path.Combine(repo, "feature_a")));
    }

    [Fact]
    public void The_manage_menu_offers_rename_for_agents_and_orchestrators()
    {
        Assert.Contains(AgentDisposal.For(hidden: false, orchestrator: false), e => e.Key == "r");
        Assert.Contains(AgentDisposal.For(hidden: false, orchestrator: true), e => e.Key == "r");
    }

    private sealed class MemoryStore : IAgentStore
    {
        private readonly List<AgentRecord> _agents = [];

        public void Save(string project, AgentRecord agent)
        {
            _agents.RemoveAll(a => PathKey.Same(a.Worktree, agent.Worktree));
            _agents.Add(agent);
        }

        public IReadOnlyList<AgentRecord> List(string project) => _agents.ToList();

        public void Remove(string project, string worktree) =>
            _agents.RemoveAll(a => PathKey.Same(a.Worktree, worktree));
    }
}
