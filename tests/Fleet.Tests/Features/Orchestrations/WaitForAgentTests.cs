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

    private WaitForAgentHandler Handler(Action<int>? onPoll = null, int quietSeconds = 60) => new(
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
        TimeSpan.FromSeconds(quietSeconds));

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
        var agent = Agent("x", OrchestrationStatus.Done) with
        {
            ReportedAt = "2026-10-28T11:00:00.0000000+00:00",
            InstructedAt = "2026-10-28T11:30:00.0000000+00:00",
        };
        _store.Agents[0] = agent;
        _texts[agent.Worktree] = "> ";

        var result = await Handler().HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.StartsWith("api/x: idle", result.Value);
    }

    [Fact]
    public async Task A_done_report_after_the_last_instruction_is_current()
    {
        _store.Agents[0] = Agent("x", OrchestrationStatus.Done) with
        {
            Summary = "endpoint added",
            ReportedAt = "2026-10-28T11:31:00.0000000+00:00",
            InstructedAt = "2026-10-28T11:30:00.0000000+00:00",
        };

        var result = await Handler().HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.Equal("api/x: done — endpoint added", result.Value);
    }

    [Fact]
    public async Task A_done_report_for_an_agent_never_instructed_is_current()
    {
        _store.Agents[0] = Agent("x", OrchestrationStatus.Failed) with { ReportedAt = "2026-10-28T11:00:00.0000000+00:00" };

        var result = await Handler().HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.Equal("api/x: failed", result.Value);
    }

    [Fact]
    public async Task A_done_without_a_report_time_after_an_instruction_is_stale()
    {
        var agent = Agent("x", OrchestrationStatus.Done) with { InstructedAt = "2026-10-28T11:30:00.0000000+00:00" };
        _store.Agents[0] = agent;
        _texts[agent.Worktree] = "> ";

        var result = await Handler().HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.StartsWith("api/x: idle", result.Value);
    }

    [Fact]
    public async Task The_instruction_file_is_not_consulted_for_staleness()
    {
        var agent = Agent("x", OrchestrationStatus.Done) with { ReportedAt = "2026-10-28T11:00:00.0000000+00:00" };
        _store.Agents[0] = agent;
        var instruction = Path.Combine(agent.Worktree, ".fleet", AgentHarness.AgentInstructionFile);
        Directory.CreateDirectory(Path.GetDirectoryName(instruction)!);
        File.WriteAllText(instruction, "a notice");
        File.SetLastWriteTimeUtc(instruction, new DateTime(2026, 10, 28, 11, 30, 0, DateTimeKind.Utc));

        var result = await Handler().HandleAsync("p", "api", "x", TimeSpan.FromMinutes(5));

        Assert.Equal("api/x: done", result.Value);
    }

    [Theory]
    [InlineData("claude", 0)]
    [InlineData("claude", -5)]
    [InlineData("codex", 0)]
    [InlineData("pi", 0)]
    [InlineData("opencode", 0)]
    [InlineData("", 0)]
    public void Without_a_timeout_the_wait_is_45_seconds_and_not_clamped(string harness, int asked)
    {
        var timeout = WaitForAgentHandler.TimeoutFrom(asked, harness);

        Assert.Equal(TimeSpan.FromSeconds(45), timeout.Value);
        Assert.False(timeout.Clamped);
    }

    [Theory]
    [InlineData("codex", 30, 30, false)]
    [InlineData("codex", 55, 55, false)]
    [InlineData("codex", 56, 55, true)]
    [InlineData("codex", 300, 55, true)]
    [InlineData("pi", 3600, 55, true)]
    [InlineData("opencode", 120, 55, true)]
    [InlineData("claude", 120, 120, false)]
    [InlineData("claude", 300, 300, false)]
    [InlineData("claude", 301, 300, true)]
    [InlineData("claude", 99999, 300, true)]
    [InlineData("", 300, 55, true)]
    [InlineData("somethingelse", 300, 55, true)]
    public void A_timeout_is_clamped_to_the_cap_of_the_calling_harness(
        string harness, int asked, int seconds, bool clamped)
    {
        var timeout = WaitForAgentHandler.TimeoutFrom(asked, harness);

        Assert.Equal(TimeSpan.FromSeconds(seconds), timeout.Value);
        Assert.Equal(clamped, timeout.Clamped);
    }

    [Fact]
    public async Task A_clamped_wait_that_runs_out_says_it_was_clamped_and_for_which_harness()
    {
        var agent = Agent("x");
        var text = 0;
        _texts[agent.Worktree] = "start";

        var result = await Handler(_ => _texts[agent.Worktree] = $"line {++text}")
            .HandleAsync("p", "api", "x", WaitForAgentHandler.TimeoutFrom(300, "codex"));

        Assert.StartsWith("api/x: still working after 55s", result.Value);
        Assert.Contains("clamped to 55s for codex", result.Value);
        Assert.Contains("wait_for_agent again", result.Value);
    }

    [Fact]
    public async Task A_clamped_wait_says_so_in_a_done_report()
    {
        var agent = Agent("x", OrchestrationStatus.Done) with { Summary = "endpoint added" };
        _store.Agents[0] = agent;

        var result = await Handler().HandleAsync("p", "api", "x", WaitForAgentHandler.TimeoutFrom(300, "codex"));

        Assert.StartsWith("api/x: done — endpoint added", result.Value);
        Assert.Contains("clamped to 55s for codex", result.Value);
    }

    [Fact]
    public async Task A_clamped_wait_says_so_in_a_failed_report()
    {
        Agent("x", OrchestrationStatus.Failed);

        var result = await Handler().HandleAsync("p", "api", "x", WaitForAgentHandler.TimeoutFrom(300, "pi"));

        Assert.StartsWith("api/x: failed", result.Value);
        Assert.Contains("clamped to 55s for pi", result.Value);
    }

    [Fact]
    public async Task A_clamped_wait_says_so_when_the_agent_goes_idle()
    {
        var agent = Agent("x");
        _texts[agent.Worktree] = "> ";

        var result = await Handler(quietSeconds: 30)
            .HandleAsync("p", "api", "x", WaitForAgentHandler.TimeoutFrom(300, "opencode"));

        Assert.StartsWith("api/x: idle", result.Value);
        Assert.Contains("clamped to 55s for opencode", result.Value);
    }

    [Fact]
    public async Task A_clamped_wait_says_so_for_an_agent_that_is_not_open()
    {
        Agent("x");

        var result = await Handler().HandleAsync("p", "api", "x", WaitForAgentHandler.TimeoutFrom(300, "codex"));

        Assert.StartsWith("api/x: not open", result.Value);
        Assert.Contains("clamped to 55s for codex", result.Value);
    }

    [Fact]
    public async Task A_wait_that_is_not_clamped_says_nothing_in_a_done_report()
    {
        Agent("x", OrchestrationStatus.Done);

        var result = await Handler().HandleAsync("p", "api", "x", WaitForAgentHandler.TimeoutFrom(30, "codex"));

        Assert.Equal("api/x: done", result.Value);
    }

    [Fact]
    public async Task A_clamped_wait_for_an_unknown_caller_names_it_as_unknown()
    {
        var agent = Agent("x");
        var text = 0;
        _texts[agent.Worktree] = "start";

        var result = await Handler(_ => _texts[agent.Worktree] = $"line {++text}")
            .HandleAsync("p", "api", "x", WaitForAgentHandler.TimeoutFrom(300, string.Empty));

        Assert.Contains("clamped to 55s for an unknown harness", result.Value);
    }

    [Fact]
    public async Task A_wait_that_is_not_clamped_does_not_mention_clamping()
    {
        var agent = Agent("x");
        var text = 0;
        _texts[agent.Worktree] = "start";

        var result = await Handler(_ => _texts[agent.Worktree] = $"line {++text}")
            .HandleAsync("p", "api", "x", WaitForAgentHandler.TimeoutFrom(30, "codex"));

        Assert.StartsWith("api/x: still working after 30s", result.Value);
        Assert.DoesNotContain("clamped", result.Value);
    }

    private sealed class Store : IAgentStore
    {
        public List<AgentRecord> Agents { get; } = [];

        public void Save(string project, AgentRecord agent) => Agents.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Agents;

        public bool Replace(string project, AgentRecord agent) => true;

        public bool Update(string project, string worktree, Func<AgentRecord, AgentRecord> change) =>
            List(project).FirstOrDefault(a => a.Worktree == worktree) is { } current && Replace(project, change(current));

        public bool Remove(string project, string worktree) => true;
    }
}
