using Fleet.Cli.Composition;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Messaging;

namespace Fleet.Tests.Cli.Composition;

public sealed class CallerHarnessesTests
{
    private readonly Store _store = new();

    private string Of(string caller) => new CallerHarnesses("p", caller, _store).Current();

    private void Add(string repository, string branch, string harness, string orchestratorHarness = "") =>
        _store.Agents.Add(new AgentRecord(
            Path.Combine("w", repository, branch), repository, branch, harness, "origin/main", false,
            OrchestratorHarness: orchestratorHarness));

    [Fact]
    public void The_main_orchestrator_with_no_caller_is_claude() =>
        Assert.Equal(AgentHarness.Claude, Of(string.Empty));

    [Fact]
    public void A_repository_agent_is_known_by_its_harness()
    {
        Add("api", "feat/x", HarnessRegistry.CodexName);

        Assert.Equal(HarnessRegistry.CodexName, Of("agent:api/feat/x"));
    }

    [Fact]
    public void A_sub_orchestrator_is_known_by_the_harness_it_runs()
    {
        Add(string.Empty, "fix-login", AgentHarness.Orchestrator, HarnessRegistry.PiName);

        Assert.Equal(HarnessRegistry.PiName, Of("fix-login"));
    }

    [Theory]
    [InlineData("agent:api/ghost")]
    [InlineData("ghost")]
    public void A_caller_that_is_not_in_the_store_is_unknown(string caller)
    {
        Add("api", "feat/x", HarnessRegistry.CodexName);

        Assert.Equal(HarnessRoute.Unknown, Of(caller));
    }

    private sealed class Store : IAgentStore
    {
        public List<AgentRecord> Agents { get; } = [];

        public void Save(string project, AgentRecord agent) => Agents.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Agents;

        public bool Replace(string project, AgentRecord agent) => true;

        public bool Update(string project, string worktree, Func<AgentRecord, AgentRecord> change) => true;

        public bool Remove(string project, string worktree) => true;
    }
}
