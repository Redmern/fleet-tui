using Fleet.Cli.Composition;
using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Platform.Harness;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Cli;

public sealed class SubDispatcherTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly MemoryStore _store = new();

    public SubDispatcherTests() => Directory.CreateDirectory(_root);

    private SubDispatcher Dispatcher =>
        new(new DispatchHandler(new FakeMuxDriver(), _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero));

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

    [Fact]
    public async Task A_ticket_is_dispatched_to_a_top_level_sub_orchestrator_and_its_slug_comes_back()
    {
        var result = await Dispatcher.DispatchAsync("techweb", _root, "fix the login page");

        Assert.True(result.Succeeded, result.Error);

        var sub = Assert.Single(_store.Saved);
        Assert.True(AgentHarness.IsOrchestrator(sub.Harness));
        Assert.Equal(result.Value, sub.Branch);
        Assert.Equal(string.Empty, sub.Owner);
    }

    [Fact]
    public async Task A_dispatch_the_handler_refuses_comes_back_as_its_error()
    {
        var result = await Dispatcher.DispatchAsync("techweb", _root, "   ");

        Assert.False(result.Succeeded);
        Assert.Empty(_store.Saved);
    }

    private sealed class MemoryStore : IAgentStore
    {
        public List<AgentRecord> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Saved;

        public void Remove(string project, string worktree) { }
    }
}
