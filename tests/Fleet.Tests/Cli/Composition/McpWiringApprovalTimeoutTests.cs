using Fleet.Cli.Composition;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Harness;

namespace Fleet.Tests.Cli.Composition;

public sealed class McpWiringApprovalTimeoutTests
{
    private readonly Store _store = new();

    private TimeSpan? WaitFor(string caller) =>
        McpWiring.ApprovalTimeout(new CallerHarnesses("p", caller, _store))();

    private void Add(string repository, string branch, string harness) =>
        _store.Agents.Add(new AgentRecord(
            Path.Combine("w", repository, branch), repository, branch, harness, "origin/main", false));

    [Fact]
    public void A_codex_agent_is_asked_within_its_sixty_second_limit()
    {
        Add("api", "feat/x", HarnessRegistry.CodexName);

        Assert.Equal(TimeSpan.FromSeconds(55), WaitFor("agent:api/feat/x"));
    }

    [Fact]
    public void A_claude_agent_gets_the_two_minute_default()
    {
        Add("api", "feat/y", HarnessRegistry.ClaudeName);

        Assert.Equal(TimeSpan.FromSeconds(120), WaitFor("agent:api/feat/y"));
    }

    [Fact]
    public void The_main_pane_is_claude()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), WaitFor(string.Empty));
    }

    [Fact]
    public void A_caller_nobody_knows_gets_the_strictest_client_limit()
    {
        Assert.Equal(TimeSpan.FromSeconds(55), WaitFor("agent:api/ghost"));
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
