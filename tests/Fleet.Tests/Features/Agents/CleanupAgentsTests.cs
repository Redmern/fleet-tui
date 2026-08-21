using Fleet.Features.Agents.CleanupAgents;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;

namespace Fleet.Tests.Features.Agents;

public sealed class CleanupAgentsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public CleanupAgentsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Removes_records_whose_worktree_is_gone_and_keeps_the_rest()
    {
        var alive = Path.Combine(_root, "alive");
        Directory.CreateDirectory(alive);

        var store = new MemoryStore();
        store.Save("techweb", Agent(alive, "feature/alive"));
        store.Save("techweb", Agent(Path.Combine(_root, "gone"), "feature/gone"));

        var summary = new CleanupHandler(store).Handle("techweb");

        Assert.Contains("1 agent record(s)", summary);
        Assert.Contains("feature/gone", summary);
        Assert.Equal(["feature/alive"], store.List("techweb").Select(a => a.Branch));
    }

    [Fact]
    public void Reports_when_nothing_is_stale()
    {
        var summary = new CleanupHandler(new MemoryStore()).Handle("techweb");

        Assert.Contains("nothing stale", summary);
    }

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

    private static AgentRecord Agent(string worktree, string branch) =>
        new(worktree, "backend", branch, "nvim", "origin/main", true);

    private sealed class MemoryStore : IAgentStore
    {
        private readonly List<AgentRecord> _records = [];

        public void Save(string project, AgentRecord agent) => _records.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => _records;

        public void Remove(string project, string worktree) =>
            _records.RemoveAll(a => a.Worktree == worktree);
    }
}
