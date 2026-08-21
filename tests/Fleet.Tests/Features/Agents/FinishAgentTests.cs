using Fleet.Features.Agents.FinishAgent;
using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;

namespace Fleet.Tests.Features.Agents;

public sealed class FinishAgentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly GitRunner _git = new();

    public FinishAgentTests() => Directory.CreateDirectory(_root);

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

    [Fact]
    public async Task Fast_forwards_the_agents_branch_into_the_base_worktree()
    {
        var repo = await RepositoryAsync();
        var agent = await AgentAsync(repo, "feature/x");

        File.WriteAllText(Path.Combine(agent.Worktree, "work.txt"), "done");
        await _git.RunAsync(agent.Worktree, ["add", "."]);
        await CommitAsync(agent.Worktree, "work");

        var finished = await new FinishAgentHandler(_git).HandleAsync(agent, push: false);

        Assert.True(finished.Succeeded, finished.Error);
        Assert.Contains("main", finished.Value);
        Assert.True(File.Exists(Path.Combine(repo, "main", "work.txt")));
    }

    [Fact]
    public async Task Refuses_when_the_agent_works_on_the_base_itself()
    {
        var agent = new AgentRecord(
            Path.Combine(_root, "w"), "backend", "main", "nvim", "origin/main", true);

        var finished = await new FinishAgentHandler(_git).HandleAsync(agent, push: false);

        Assert.False(finished.Succeeded);
        Assert.Contains("nothing to merge", finished.Error);
    }

    private async Task<string> RepositoryAsync()
    {
        var created = await new AddRepositoryHandler(_git)
            .HandleAsync(AddRepositoryCommand.CreateNew(_root, "backend", "main"));

        Assert.True(created.Succeeded, created.Error);

        return created.Value!.Path;
    }

    private async Task<AgentRecord> AgentAsync(string repo, string branch)
    {
        var made = await new NewAgentHandler(_git, new FakeMuxDriver(), new MemoryStore())
            .HandleAsync(new NewAgentCommand("proj", "backend", repo, branch, string.Empty, "claude"));

        Assert.True(made.Succeeded, made.Error);

        return made.Value!;
    }

    private async Task CommitAsync(string worktree, string message)
    {
        var committed = await _git.RunAsync(
            worktree,
            ["-c", "user.email=test@fleet", "-c", "user.name=fleet", "commit", "-m", message]);

        Assert.True(committed.Ok, committed.Message);
    }

    private sealed class MemoryStore : IAgentStore
    {
        private readonly List<AgentRecord> _records = [];

        public void Save(string project, AgentRecord agent) => _records.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => _records;

        public void Remove(string project, string worktree) =>
            _records.RemoveAll(a => a.Worktree == worktree);
    }
}
