using System.Collections.Concurrent;
using Fleet.Features.Mcp.ServeMcp;
using Fleet.Ports;
using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Settings;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Mcp;

public sealed class McpDispatcherTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly FakeSettings _settings = new();

    private readonly FakeApprovals _approvals = new();

    private readonly FakeLog _log = new();

    private int _performed;

    private static McpRequest Request(string tool) => new(tool, new Dictionary<string, string>());

    private McpDispatcher Dispatcher(string caller = "") => new(
        McpCaller.AtRoot("techweb", "C:/repos/techweb") with { Caller = caller },
        _settings,
        _approvals,
        _log,
        (_, _) =>
        {
            _performed++;
            return Task.FromResult(McpResult.Ok("done"));
        });

    private McpDispatcher Performing(Func<McpRequest, CancellationToken, Task<McpResult>> perform) => new(
        McpCaller.AtRoot("techweb", "C:/repos/techweb"), _settings, _approvals, _log, perform);

    [Fact]
    public async Task An_unknown_tool_never_reaches_the_handler()
    {
        var result = await Dispatcher().HandleAsync(Request("teleport"));

        Assert.True(result.IsError);
        Assert.Equal(0, _performed);
    }

    [Fact]
    public async Task A_read_tool_runs_without_asking()
    {
        var result = await Dispatcher().HandleAsync(Request("list_agents"));

        Assert.False(result.IsError);
        Assert.Equal(1, _performed);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task A_forbidden_tool_is_refused_before_the_handler()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.RemoveRepository, ActionPolicy.Forbid);

        var result = await Dispatcher().HandleAsync(Request("remove_repository"));

        Assert.True(result.IsError);
        Assert.Equal(0, _performed);
        Assert.Contains(_log.Lines, l => l.Contains("not allowed"));
    }

    [Fact]
    public async Task An_ask_tool_prompts_the_dashboard_and_runs_when_allowed()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        _approvals.Answer = ApprovalOutcome.Allow;

        var result = await Dispatcher().HandleAsync(Request("new_agent"));

        Assert.False(result.IsError);
        Assert.Equal(1, _performed);
        Assert.Single(_approvals.Asked);
    }

    [Fact]
    public async Task An_ask_names_the_caller_and_carries_the_deadline_the_wiring_chose()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        var dispatcher = new McpDispatcher(
            McpCaller.AtRoot("techweb", "C:/repos/techweb") with { Caller = "agent:backend/login" },
            _settings,
            _approvals,
            _log,
            (_, _) => Task.FromResult(McpResult.Ok("done")),
            approvalTimeout: () => TimeSpan.FromSeconds(55));

        await dispatcher.HandleAsync(Request("new_agent"));

        var asked = Assert.Single(_approvals.Asked);
        Assert.Equal("agent:backend/login", asked.Caller);
        Assert.Equal(TimeSpan.FromSeconds(55), asked.Timeout);
    }

    [Fact]
    public async Task A_queued_ask_counts_the_time_it_waited_for_its_turn_and_expires_without_a_prompt()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        _approvals.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new Clock();
        var dispatcher = new McpDispatcher(
            McpCaller.AtRoot("techweb", "C:/repos/techweb") with { Caller = "agent:backend/login" },
            _settings,
            _approvals,
            _log,
            (_, _) =>
            {
                _performed++;
                return Task.FromResult(McpResult.Ok("done"));
            },
            approvalTimeout: () => TimeSpan.FromSeconds(55),
            time: clock);

        var first = dispatcher.HandleAsync(Request("new_agent"));
        var second = dispatcher.HandleAsync(Request("new_agent"));
        await Until(() => _approvals.Asked.Count == 1);

        clock.Now += TimeSpan.FromSeconds(56);
        _approvals.Hold.SetResult();
        var late = await second.WaitAsync(Patience);
        var refused = await first.WaitAsync(Patience);

        Assert.True(refused.IsError);
        Assert.Contains("so the action was not run", refused.Text);
        Assert.Equal(0, _performed);
        Assert.True(late.IsError);
        Assert.Equal(
            "The approval prompt for agent:backend/login in techweb was never shown: the 55 s it was allowed ran out while it waited behind other approvals",
            late.Text);
        Assert.Single(_approvals.Asked);
    }

    [Fact]
    public async Task An_allowed_action_that_waited_for_its_turn_past_the_deadline_is_not_run()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new ConcurrentQueue<string>();
        var clock = new Clock();
        var dispatcher = new McpDispatcher(
            McpCaller.AtRoot("techweb", "C:/repos/techweb") with { Caller = "agent:backend/login" },
            _settings,
            _approvals,
            _log,
            async (request, _) =>
            {
                started.Enqueue(request.Tool);
                await release.Task.WaitAsync(Patience);

                return McpResult.Ok("done");
            },
            approvalTimeout: () => TimeSpan.FromSeconds(55),
            time: clock);

        var holding = dispatcher.HandleAsync(Request("tell_agent"));
        await Until(() => started.Count == 1);
        var allowed = dispatcher.HandleAsync(Request("new_agent"));
        await Until(() => _approvals.Asked.Count == 1);

        clock.Now += TimeSpan.FromSeconds(56);
        release.SetResult();
        var result = await allowed.WaitAsync(Patience);
        await holding.WaitAsync(Patience);

        Assert.True(result.IsError);
        Assert.Contains("so the action was not run", result.Text);
        Assert.Equal(["tell_agent"], started);
    }

    [Fact]
    public async Task A_call_cancelled_while_it_waits_for_its_turn_after_an_allow_never_runs_and_holds_nothing()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new ConcurrentQueue<string>();
        using var cancel = new CancellationTokenSource();
        var dispatcher = new McpDispatcher(
            McpCaller.AtRoot("techweb", "C:/repos/techweb"),
            _settings,
            _approvals,
            _log,
            async (request, _) =>
            {
                started.Enqueue(request.Tool);
                await release.Task.WaitAsync(Patience);

                return McpResult.Ok("done");
            },
            approvalTimeout: () => TimeSpan.FromSeconds(55),
            time: new Clock());

        var holding = dispatcher.HandleAsync(Request("tell_agent"));
        await Until(() => started.Count == 1);
        var waiting = dispatcher.HandleAsync(Request("new_agent"), cancel.Token);
        await Until(() => _approvals.Asked.Count == 1);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        release.SetResult();
        await holding.WaitAsync(Patience);
        var after = await dispatcher.HandleAsync(Request("stop_agent")).WaitAsync(Patience);

        Assert.False(after.IsError);
        Assert.Equal(["tell_agent", "stop_agent"], started);
    }

    [Fact]
    public async Task An_allowed_action_that_gets_its_turn_in_time_runs()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        var dispatcher = new McpDispatcher(
            McpCaller.AtRoot("techweb", "C:/repos/techweb"),
            _settings,
            _approvals,
            _log,
            (_, _) =>
            {
                _performed++;
                return Task.FromResult(McpResult.Ok("done"));
            },
            approvalTimeout: () => TimeSpan.FromSeconds(55),
            time: new Clock());

        var result = await dispatcher.HandleAsync(Request("new_agent")).WaitAsync(Patience);

        Assert.False(result.IsError);
        Assert.Equal(1, _performed);
    }

    [Fact]
    public async Task An_ask_carries_the_moment_the_call_arrived_so_the_deadline_starts_there()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        var clock = new Clock();
        var dispatcher = new McpDispatcher(
            McpCaller.AtRoot("techweb", "C:/repos/techweb"),
            _settings,
            _approvals,
            _log,
            (_, _) => Task.FromResult(McpResult.Ok("done")),
            approvalTimeout: () => TimeSpan.FromSeconds(55),
            time: clock);

        await dispatcher.HandleAsync(Request("new_agent"));

        Assert.Equal(clock.Now, Assert.Single(_approvals.Asked).Arrived);
    }

    [Fact]
    public async Task A_denied_ask_returns_the_reason_and_skips_the_handler()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        _approvals.Answer = ApprovalOutcome.Deny("you said no");

        var result = await Dispatcher().HandleAsync(Request("new_agent"));

        Assert.True(result.IsError);
        Assert.Equal("you said no", result.Text);
        Assert.Equal(0, _performed);
    }

    [Fact]
    public async Task A_claude_only_ask_passes_through_because_claude_already_prompted()
    {
        _settings.Config = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Ask)
            .With(HarnessTool.NewAgent, AskChannel.ClaudePermission);

        var result = await Dispatcher().HandleAsync(Request("new_agent"));

        Assert.False(result.IsError);
        Assert.Equal(1, _performed);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task A_handler_that_fails_is_audited_as_denied()
    {
        var dispatcher = new McpDispatcher(
            McpCaller.AtRoot("techweb", "C:/repos/techweb"),
            _settings,
            _approvals,
            _log,
            (_, _) => Task.FromResult(McpResult.Error("git blew up")));

        var result = await dispatcher.HandleAsync(Request("list_agents"));

        Assert.True(result.IsError);
        Assert.Contains(_log.Lines, l => l.Contains("git blew up"));
    }

    [Theory]
    [InlineData("wait_for_agent")]
    [InlineData("list_agents")]
    [InlineData("agent_status")]
    [InlineData("list_subs")]
    [InlineData("list_repositories")]
    [InlineData("list_branches")]
    [InlineData("repository_status")]
    [InlineData("list_forwards")]
    [InlineData("log_tail")]
    public async Task A_read_only_call_runs_while_a_changing_call_is_still_running(string readOnly)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var dispatcher = Performing(async (request, _) =>
        {
            if (request.Tool == "new_agent")
            {
                await release.Task.WaitAsync(Patience);
            }

            return McpResult.Ok(request.Tool);
        });

        var changing = dispatcher.HandleAsync(Request("new_agent"));
        var read = await dispatcher.HandleAsync(Request(readOnly)).WaitAsync(Patience);

        Assert.Equal(readOnly, read.Text);
        Assert.False(changing.IsCompleted);

        release.SetResult();

        Assert.Equal("new_agent", (await changing.WaitAsync(Patience)).Text);
    }

    [Fact]
    public async Task Two_read_only_calls_run_at_the_same_time()
    {
        var bothRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;

        var dispatcher = Performing(async (_, _) =>
        {
            if (Interlocked.Increment(ref running) == 2)
            {
                bothRunning.SetResult();
            }

            await bothRunning.Task.WaitAsync(Patience);

            return McpResult.Ok("done");
        });

        var results = await Task.WhenAll(
            dispatcher.HandleAsync(Request("wait_for_agent")),
            dispatcher.HandleAsync(Request("wait_for_agent")));

        Assert.All(results, result => Assert.False(result.IsError));
    }

    [Theory]
    [InlineData("tell_agent", "tell_agent")]
    [InlineData("new_agent", "open_agent")]
    [InlineData("dispatch", "report")]
    public async Task Two_changing_calls_run_one_at_a_time(string first, string second)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new ConcurrentQueue<string>();

        var dispatcher = Performing(async (request, _) =>
        {
            started.Enqueue(request.Tool);

            await release.Task.WaitAsync(Patience);

            return McpResult.Ok("done");
        });

        var one = dispatcher.HandleAsync(Request(first));
        var two = dispatcher.HandleAsync(Request(second));

        Assert.Equal([first], started);

        release.SetResult();
        await Task.WhenAll(one, two).WaitAsync(Patience);

        Assert.Equal([first, second], started);
    }

    [Fact]
    public async Task A_changing_call_cancelled_while_it_waits_its_turn_never_runs_and_holds_nothing()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        var started = new ConcurrentQueue<string>();

        var dispatcher = Performing(async (request, _) =>
        {
            started.Enqueue(request.Tool);

            await release.Task.WaitAsync(Patience);

            return McpResult.Ok("done");
        });

        var running = dispatcher.HandleAsync(Request("new_agent"));
        var waiting = dispatcher.HandleAsync(Request("tell_agent"), cancel.Token);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        release.SetResult();
        await running.WaitAsync(Patience);
        await dispatcher.HandleAsync(Request("stop_agent")).WaitAsync(Patience);

        Assert.Equal(["new_agent", "stop_agent"], started);
    }

    [Fact]
    public async Task A_changing_call_cancelled_while_it_runs_lets_the_next_one_run()
    {
        using var cancel = new CancellationTokenSource();

        var dispatcher = Performing(async (request, ct) =>
        {
            if (request.Tool == "new_agent")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).WaitAsync(Patience, CancellationToken.None);
            }

            return McpResult.Ok(request.Tool);
        });

        var running = dispatcher.HandleAsync(Request("new_agent"), cancel.Token);
        var next = dispatcher.HandleAsync(Request("tell_agent"));

        Assert.False(next.IsCompleted);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        Assert.Equal("tell_agent", (await next.WaitAsync(Patience)).Text);
    }

    [Fact]
    public async Task Two_calls_that_ask_are_put_to_the_user_one_at_a_time()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        _approvals.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Dispatcher();

        var first = dispatcher.HandleAsync(Request("new_agent"));
        var second = dispatcher.HandleAsync(Request("new_agent"));

        Assert.Single(_approvals.Asked);

        _approvals.Hold.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Patience);

        Assert.Equal(2, _approvals.Asked.Count);
        Assert.Equal(2, _performed);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var cancel = new CancellationTokenSource(Patience);

        while (!condition())
        {
            await Task.Yield();
            cancel.Token.ThrowIfCancellationRequested();
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public SettingsConfig Config { get; set; } = SettingsConfig.Default;

        public SettingsConfig Load(string project) => Config;

        public void Save(string project, SettingsConfig config) => Config = config;
    }

    private sealed class FakeApprovals : IApprovalChannel
    {
        public ApprovalOutcome Answer { get; set; } = ApprovalOutcome.Allow;

        public ConcurrentQueue<ApprovalRequest> Asked { get; } = [];

        public TaskCompletionSource? Hold { get; set; }

        public async Task<ApprovalOutcome> AskAsync(ApprovalRequest request, CancellationToken ct = default)
        {
            Asked.Enqueue(request);

            if (Hold is not null)
            {
                await Hold.Task;
            }

            return Answer;
        }
    }

    private sealed class FakeLog : IFleetLog
    {
        public ConcurrentQueue<string> Lines { get; } = [];

        public void Swallowed(Exception e) { }

        public void Write(string line) => Lines.Enqueue(line);

        public IReadOnlyList<string> Tail(int lines) => [.. Lines];
    }

    private static McpRequest Dispatch(string? repository = null) =>
        new(
            "dispatch",
            repository is null
                ? new Dictionary<string, string> { ["message"] = "fix it" }
                : new Dictionary<string, string> { ["message"] = "fix it", ["repository"] = repository });

    [Fact]
    public async Task A_direct_dispatch_is_refused_when_the_project_forbids_new_agent()
    {
        _settings.Config = SettingsConfig.Default
            .With(HarnessTool.Dispatch, ActionPolicy.Allow)
            .With(HarnessTool.NewAgent, ActionPolicy.Forbid);

        var result = await Dispatcher().HandleAsync(Dispatch("backend"));

        Assert.True(result.IsError);
        Assert.Contains("new_agent", result.Text);
        Assert.Equal(0, _performed);
    }

    [Fact]
    public async Task A_dispatch_without_a_repository_ignores_the_new_agent_rule()
    {
        _settings.Config = SettingsConfig.Default
            .With(HarnessTool.Dispatch, ActionPolicy.Allow)
            .With(HarnessTool.NewAgent, ActionPolicy.Forbid);

        var result = await Dispatcher().HandleAsync(Dispatch());

        Assert.False(result.IsError);
        Assert.Equal(1, _performed);
    }

    [Fact]
    public async Task A_direct_dispatch_is_refused_when_dispatch_itself_is_forbidden()
    {
        _settings.Config = SettingsConfig.Default
            .With(HarnessTool.Dispatch, ActionPolicy.Forbid)
            .With(HarnessTool.NewAgent, ActionPolicy.Allow);

        var result = await Dispatcher().HandleAsync(Dispatch("backend"));

        Assert.True(result.IsError);
        Assert.Equal(0, _performed);
    }

    [Fact]
    public async Task A_direct_dispatch_asks_for_both_tools_when_both_ask()
    {
        _settings.Config = SettingsConfig.Default
            .With(HarnessTool.Dispatch, ActionPolicy.Ask)
            .With(HarnessTool.NewAgent, ActionPolicy.Ask);

        var result = await Dispatcher().HandleAsync(Dispatch("backend"));

        Assert.False(result.IsError);
        Assert.Equal(["dispatch", "new_agent"], _approvals.Asked.Select(a => a.Tool));
        Assert.Equal(1, _performed);
    }

    [Fact]
    public async Task A_sub_dispatching_to_a_repository_gets_new_agent_without_asking_like_new_agent_itself()
    {
        _settings.Config = SettingsConfig.Default
            .With(HarnessTool.Dispatch, ActionPolicy.Allow)
            .With(HarnessTool.NewAgent, ActionPolicy.Ask);

        var result = await Dispatcher("upgrade").HandleAsync(Dispatch("backend"));

        Assert.False(result.IsError);
        Assert.Empty(_approvals.Asked);
        Assert.Equal(1, _performed);
    }

    [Fact]
    public async Task A_denied_new_agent_stops_a_direct_dispatch_after_dispatch_was_allowed()
    {
        _settings.Config = SettingsConfig.Default
            .With(HarnessTool.Dispatch, ActionPolicy.Allow)
            .With(HarnessTool.NewAgent, ActionPolicy.Ask);
        _approvals.Answer = ApprovalOutcome.Deny("not today");

        var result = await Dispatcher().HandleAsync(Dispatch("backend"));

        Assert.True(result.IsError);
        Assert.Equal("not today", result.Text);
        Assert.Equal(0, _performed);
    }
}
