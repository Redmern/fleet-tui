using Fleet.Features.Orchestrations.WaitForAgent;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class WaitForAgentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly Store _store = new();

    private readonly Dictionary<string, string?> _texts = [];

    private DateTime _now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private int _polls;

    public WaitForAgentTests() => Directory.CreateDirectory(_root);

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

    private WaitForAgentHandler Handler(Action<int>? onPoll = null) => new(
        _store,
        (agent, _) => Task.FromResult(_texts.TryGetValue(agent.Worktree, out var text) ? text : null),
        () => _now,
        (span, _) =>
        {
            _now += span;
            _polls++;
            onPoll?.Invoke(_polls);
            return Task.CompletedTask;
        },
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(60));

    private AgentRecord Agent(string branch, string status = OrchestrationStatus.Working, string harness = AgentHarness.Opencode)
    {
        var record = new AgentRecord(
            Path.Combine(_root, branch), "api", branch, harness, "origin/main", false, Status: status);
        Directory.CreateDirectory(record.Worktree);
        _store.Agents.Add(record);

        return record;
    }

    [Fact]
    public async Task A_done_report_returns_at_once_with_its_summary()
    {
        var agent = Agent("x", OrchestrationStatus.Done) with { Summary = "endpoint added" };
        _store.Agents[0] = agent;

        var result = await Handler().HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.Equal("api/x: done — endpoint added", result.Value);
        Assert.Equal(0, _polls);
    }

    [Fact]
    public async Task A_report_that_arrives_while_waiting_ends_the_wait()
    {
        var agent = Agent("x");
        var text = 0;
        _texts[agent.Worktree] = "thinking 0";

        var result = await Handler(poll =>
        {
            _texts[agent.Worktree] = $"thinking {++text}";

            if (poll == 3)
            {
                _store.Agents[0] = agent with { Status = OrchestrationStatus.Failed, Summary = "tests red" };
            }
        }).HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.Equal("api/x: failed — tests red", result.Value);
        Assert.Equal(3, _polls);
    }

    [Fact]
    public async Task A_pane_without_output_for_the_window_is_idle()
    {
        var agent = Agent("x");
        _texts[agent.Worktree] = "> ";

        var result = await Handler().HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.StartsWith("api/x: idle", result.Value);
        Assert.Contains("60s", result.Value);
        Assert.Equal(6, _polls);
    }

    [Fact]
    public async Task A_busy_pane_runs_into_the_timeout()
    {
        var agent = Agent("x");
        var text = 0;
        _texts[agent.Worktree] = "start";

        var result = await Handler(_ => _texts[agent.Worktree] = $"line {++text}")
            .HandleAsync("p", "api", "x", TimeSpan.FromSeconds(45));

        Assert.StartsWith("api/x: still working after 45s", result.Value);
        Assert.Contains("wait_for_agent again", result.Value);
    }

    [Fact]
    public async Task A_closed_agent_is_not_waited_on()
    {
        Agent("x");

        var result = await Handler().HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.StartsWith("api/x: not open", result.Value);
    }

    [Fact]
    public async Task An_unknown_agent_fails()
    {
        var result = await Handler().HandleAsync("p", "api", "ghost", TimeSpan.FromMinutes(5));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_sub_orchestrator_is_found_by_its_slug_without_a_repository()
    {
        _store.Agents.Add(new AgentRecord(
            Path.Combine(_root, "sub"), string.Empty, "fix-login", AgentHarness.Orchestrator, string.Empty, false,
            Status: OrchestrationStatus.Done));

        var result = await Handler().HandleAsync("p", string.Empty, "fix-login", TimeSpan.FromMinutes(5));

        Assert.Equal("fix-login: done", result.Value);
    }

    [Fact]
    public async Task A_done_report_from_before_the_last_instruction_is_stale()
    {
        var agent = Agent("x", OrchestrationStatus.Done) with { ReportedAt = "2026-10-28T11:00:00.0000000+00:00" };
        _store.Agents[0] = agent;
        var instruction = Path.Combine(agent.Worktree, ".fleet", AgentHarness.AgentInstructionFile);
        Directory.CreateDirectory(Path.GetDirectoryName(instruction)!);
        File.WriteAllText(instruction, "next task");
        File.SetLastWriteTimeUtc(instruction, new DateTime(2026, 10, 28, 11, 30, 0, DateTimeKind.Utc));
        _texts[agent.Worktree] = "> ";

        var result = await Handler().HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.StartsWith("api/x: idle", result.Value);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(-5, 300)]
    [InlineData(30, 30)]
    [InlineData(99999, 3600)]
    public void The_timeout_defaults_and_is_capped(int asked, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), WaitForAgentHandler.TimeoutFrom(asked));

    private sealed class Store : IAgentStore
    {
        public List<AgentRecord> Agents { get; } = [];

        public void Save(string project, AgentRecord agent) => Agents.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Agents;

        public bool Replace(string project, AgentRecord agent) => true;

        public bool Remove(string project, string worktree) => true;
    }
}
