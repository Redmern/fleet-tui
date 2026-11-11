using Fleet.Features.Agents.ChangeHarness;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public sealed class ChangeHarnessTests
{
    private static readonly AgentRecord Agent = new("/w", "api", "feat/x", AgentHarness.Claude, "main", false);

    [Fact]
    public void An_unregistered_harness_is_refused_and_nothing_is_stored()
    {
        var store = new RecordingStore();

        var changed = new ChangeHarnessHandler(store).Handle("p", Agent, "codexx");

        Assert.False(changed.Succeeded);
        Assert.Contains("nvim, claude, opencode", changed.Error);
        Assert.Empty(store.Replaced);
    }

    [Fact]
    public void A_registered_harness_is_stored()
    {
        var store = new RecordingStore();

        var changed = new ChangeHarnessHandler(store).Handle("p", Agent, " opencode ");

        Assert.True(changed.Succeeded);
        Assert.Equal(AgentHarness.Opencode, Assert.Single(store.Replaced).Harness);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<AgentRecord> Replaced { get; } = [];

        public void Save(string project, AgentRecord agent) => Replaced.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Replaced;

        public bool Replace(string project, AgentRecord agent)
        {
            Replaced.Add(agent);
            return true;
        }

        public bool Remove(string project, string worktree) => true;
    }
}
