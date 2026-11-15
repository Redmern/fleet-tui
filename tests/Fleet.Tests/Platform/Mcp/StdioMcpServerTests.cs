using System.Globalization;
using System.Text.Json;
using Fleet.Platform.Mcp;
using Fleet.Ports;
using Fleet.Ports.Mcp.Models;
using Fleet.Tests.Platform.Mcp.Support;

namespace Fleet.Tests.Platform.Mcp;

public sealed class StdioMcpServerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly FakeLog _log = new();

    private static McpServing Serving(Func<McpRequest, McpResult> perform) => new(
        "fleet",
        [new McpToolInfo("list_agents", "List agents", """{"type":"object"}""")],
        (request, _) => Task.FromResult(perform(request)));

    private async Task<IReadOnlyList<string>> Run(string transcript, McpServing serving)
    {
        var output = new LineSink();

        await new StdioMcpServer(new StringReader(transcript), output, _log).RunAsync(serving).WaitAsync(Patience);

        return output.Lines;
    }

    [Fact]
    public async Task Initialize_announces_the_server_by_name()
    {
        var replies = await Run(
            """{"id":0,"method":"initialize"}""" + "\n",
            Serving(_ => McpResult.Ok("")));

        Assert.Single(replies);
        Assert.Contains("\"serverInfo\"", replies[0]);
        Assert.Contains("\"name\":\"fleet\"", replies[0]);
        Assert.Contains("\"id\":0", replies[0]);
    }

    [Fact]
    public async Task Tools_list_returns_the_served_tools()
    {
        var replies = await Run(
            """{"id":1,"method":"tools/list"}""" + "\n",
            Serving(_ => McpResult.Ok("")));

        Assert.Contains("list_agents", replies[0]);
    }

    [Fact]
    public async Task A_tool_call_reaches_the_handler_and_returns_its_text()
    {
        var replies = await Run(
            """{"id":2,"method":"tools/call","params":{"name":"list_agents","arguments":{}}}""" + "\n",
            Serving(request => McpResult.Ok($"invoked {request.Tool}")));

        Assert.Contains("invoked list_agents", replies[0]);
        Assert.Contains("\"isError\":false", replies[0]);
    }

    [Fact]
    public async Task A_notification_draws_no_reply()
    {
        var replies = await Run(
            """{"method":"notifications/initialized"}""" + "\n",
            Serving(_ => McpResult.Ok("")));

        Assert.Empty(replies);
    }

    [Fact]
    public async Task An_unknown_method_with_an_id_returns_a_method_not_found_error()
    {
        var replies = await Run(
            """{"id":9,"method":"dance"}""" + "\n",
            Serving(_ => McpResult.Ok("")));

        Assert.Contains("-32601", replies[0]);
        Assert.Contains("\"id\":9", replies[0]);
    }

    [Fact]
    public async Task A_handler_that_throws_is_reported_as_an_error_not_a_crash()
    {
        var replies = await Run(
            """{"id":3,"method":"tools/call","params":{"name":"list_agents","arguments":{}}}""" + "\n",
            Serving(_ => throw new InvalidOperationException("boom")));

        Assert.Contains("\"isError\":true", replies[0]);
        Assert.NotEmpty(_log.Swallows);
    }

    [Fact]
    public async Task A_one_at_a_time_server_runs_its_calls_in_turn_and_still_answers_a_ping()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new LineSink();
        var running = 0;
        var mostAtOnce = 0;

        var serving = new McpServing("fleet", [], async (_, _) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref mostAtOnce, now);

            await release.Task.WaitAsync(Patience);

            Interlocked.Decrement(ref running);

            return McpResult.Ok("ok");
        });

        var transcript = string.Join('\n',
            Call(1),
            Call(2),
            Call(3),
            """{"id":4,"method":"ping"}""") + "\n";

        var run = new StdioMcpServer(new StringReader(transcript), output, _log, oneCallAtATime: true)
            .RunAsync(serving);

        Assert.Equal("4", IdOf(Assert.Single(await output.WaitForLines(1))));

        release.SetResult();
        await run.WaitAsync(Patience);

        Assert.Equal(1, mostAtOnce);
        Assert.Equal(["1", "2", "3", "4"], output.Lines.Select(IdOf).Order());
    }

    private static void InterlockedMax(ref int most, int now)
    {
        int seen;

        do
        {
            seen = Volatile.Read(ref most);
        }
        while (now > seen && Interlocked.CompareExchange(ref most, now, seen) != seen);
    }

    [Fact]
    public async Task A_whole_session_answers_every_id_exactly_once_and_skips_the_notification()
    {
        var transcript = string.Join('\n',
            """{"id":0,"method":"initialize"}""",
            """{"method":"notifications/initialized"}""",
            """{"id":1,"method":"tools/list"}""",
            """{"id":2,"method":"tools/call","params":{"name":"list_agents","arguments":{}}}""") + "\n";

        var replies = await Run(transcript, Serving(_ => McpResult.Ok("ok")));

        Assert.Equal(3, replies.Count);
        Assert.Contains("serverInfo", Assert.Single(replies, r => IdOf(r) == "0"));
        Assert.Contains("list_agents", Assert.Single(replies, r => IdOf(r) == "1"));
        Assert.Contains("\"text\":\"ok\"", Assert.Single(replies, r => IdOf(r) == "2"));
    }

    [Fact]
    public async Task A_ping_is_answered_while_a_call_is_still_running()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new LineSink();
        var transcript = string.Join('\n',
            Call(1),
            """{"id":2,"method":"ping"}""") + "\n";

        var run = new StdioMcpServer(new StringReader(transcript), output, _log)
            .RunAsync(Blocking(release.Task));

        var early = await output.WaitForLines(1);

        Assert.Equal("2", IdOf(Assert.Single(early)));

        release.SetResult();
        await run.WaitAsync(Patience);

        Assert.Equal(["2", "1"], output.Lines.Select(IdOf));
    }

    [Fact]
    public async Task Two_calls_run_at_the_same_time()
    {
        var bothRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;

        var serving = new McpServing("fleet", [], async (_, _) =>
        {
            if (Interlocked.Increment(ref running) == 2)
            {
                bothRunning.SetResult();
            }

            await bothRunning.Task.WaitAsync(Patience);

            return McpResult.Ok("ok");
        });

        var replies = await Run(Call(1) + "\n" + Call(2) + "\n", serving);

        Assert.Equal(["1", "2"], replies.Select(IdOf).Order());
    }

    [Fact]
    public async Task Replies_written_at_the_same_time_never_share_a_line()
    {
        var transcript = string.Concat(Enumerable.Range(0, 60).Select(id => Call(id) + "\n"));

        var serving = new McpServing("fleet", [], async (_, _) =>
        {
            await Task.Yield();

            return McpResult.Ok("ok");
        });

        var replies = await Run(transcript, serving);

        Assert.Equal(
            Enumerable.Range(0, 60).Select(id => id.ToString(CultureInfo.InvariantCulture)),
            replies.Select(IdOf).OrderBy(int.Parse));
    }

    [Fact]
    public async Task The_server_waits_for_running_calls_when_its_input_ends()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new LineSink();

        var run = new StdioMcpServer(new StringReader(Call(1) + "\n"), output, _log)
            .RunAsync(Blocking(release.Task));

        Assert.False(run.IsCompleted);

        release.SetResult();
        await run.WaitAsync(Patience);

        Assert.Equal("1", IdOf(Assert.Single(output.Lines)));
    }

    [Theory]
    [InlineData("""{"id":1,"method":"tools/call","params":{"name":"list_agents"}}""", "1")]
    [InlineData("""{"id":"call-1","method":"tools/call","params":{"name":"list_agents"}}""", "\"call-1\"")]
    public async Task A_cancelled_call_has_its_work_cancelled_and_sends_no_result(string call, string requestId)
    {
        var cancelled = false;

        var serving = new McpServing("fleet", [], async (_, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).WaitAsync(Patience, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                cancelled = ct.IsCancellationRequested;

                throw;
            }

            return McpResult.Ok("late");
        });

        var transcript = string.Join('\n',
            call,
            """{"method":"notifications/cancelled","params":{"reason":"gave up","requestId":""" + requestId + "}}",
            """{"id":2,"method":"ping"}""") + "\n";

        var replies = await Run(transcript, serving);

        Assert.True(cancelled);
        Assert.Equal("2", IdOf(Assert.Single(replies)));
        Assert.Empty(_log.Swallows);
    }

    [Fact]
    public async Task A_cancelled_call_that_finishes_anyway_still_sends_no_result()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new LineSink();
        var transcript = string.Join('\n',
            Call(1),
            """{"method":"notifications/cancelled","params":{"requestId":1}}""") + "\n";

        var run = new StdioMcpServer(new StringReader(transcript), output, _log)
            .RunAsync(Blocking(release.Task));

        release.SetResult();
        await run.WaitAsync(Patience);

        Assert.Empty(output.Lines);
    }

    [Fact]
    public async Task Cancelling_one_call_leaves_the_others_running()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new LineSink();
        var transcript = string.Join('\n',
            Call(1),
            Call(2),
            """{"method":"notifications/cancelled","params":{"requestId":1}}""") + "\n";

        var serving = new McpServing("fleet", [], async (_, ct) =>
        {
            await release.Task.WaitAsync(Patience, ct);

            return McpResult.Ok("ok");
        });

        var run = new StdioMcpServer(new StringReader(transcript), output, _log).RunAsync(serving);

        release.SetResult();
        await run.WaitAsync(Patience);

        Assert.Equal("2", IdOf(Assert.Single(output.Lines)));
    }

    [Fact]
    public async Task A_cancellation_for_a_request_that_is_not_running_is_ignored()
    {
        var transcript = string.Join('\n',
            """{"method":"notifications/cancelled","params":{"requestId":41}}""",
            Call(1),
            """{"method":"notifications/cancelled"}""") + "\n";

        var replies = await Run(transcript, Serving(_ => McpResult.Ok("ok")));

        Assert.Equal("1", IdOf(Assert.Single(replies)));
    }

    [Fact]
    public async Task A_handler_that_gives_up_on_its_own_is_reported_as_an_error()
    {
        var replies = await Run(
            Call(1) + "\n",
            Serving(_ => throw new TaskCanceledException("an inner timeout")));

        Assert.Contains("\"isError\":true", Assert.Single(replies));
        Assert.NotEmpty(_log.Swallows);
    }

    [Fact]
    public async Task A_long_call_with_a_progress_token_reports_progress_until_it_completes()
    {
        var clock = new ManualClock();
        var output = new LineSink();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = new StdioMcpServer(
                new StringReader(Call(1, ""","_meta":{"progressToken":"tok-1"}""") + "\n"), output, _log, clock)
            .RunAsync(Blocking(release.Task, started));

        await started.Task.WaitAsync(Patience);

        clock.Advance(TimeSpan.FromSeconds(19));
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(
            """{"jsonrpc":"2.0","method":"notifications/progress","params":{"progressToken":"tok-1","progress":20}}""",
            Assert.Single(await output.WaitForLines(1)));

        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Contains("\"progress\":40", (await output.WaitForLines(2))[1]);

        release.SetResult();
        await run.WaitAsync(Patience);
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(0, clock.ActiveTimers);
        Assert.Equal(3, output.Lines.Count);
        Assert.Equal("1", IdOf(output.Lines[2]));
    }

    [Fact]
    public async Task A_numeric_progress_token_goes_back_as_a_number()
    {
        var clock = new ManualClock();
        var output = new LineSink();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = new StdioMcpServer(
                new StringReader(Call(1, ""","_meta":{"progressToken":7}""") + "\n"), output, _log, clock)
            .RunAsync(Blocking(release.Task, started));

        await started.Task.WaitAsync(Patience);
        clock.Advance(TimeSpan.FromSeconds(20));

        Assert.Contains("\"progressToken\":7,", (await output.WaitForLines(1))[0]);

        release.SetResult();
        await run.WaitAsync(Patience);
    }

    [Fact]
    public async Task A_call_without_a_progress_token_reports_no_progress()
    {
        var clock = new ManualClock();
        var output = new LineSink();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = new StdioMcpServer(new StringReader(Call(1) + "\n"), output, _log, clock)
            .RunAsync(Blocking(release.Task, started));

        await started.Task.WaitAsync(Patience);

        Assert.Equal(0, clock.ActiveTimers);

        clock.Advance(TimeSpan.FromMinutes(2));
        release.SetResult();
        await run.WaitAsync(Patience);

        Assert.Equal("1", IdOf(Assert.Single(output.Lines)));
    }

    [Fact]
    public async Task A_cancelled_call_stops_reporting_progress()
    {
        var clock = new ManualClock();
        var output = new LineSink();
        var transcript = string.Join('\n',
            Call(1, ""","_meta":{"progressToken":"tok-1"}"""),
            """{"method":"notifications/cancelled","params":{"requestId":1}}""") + "\n";

        var serving = new McpServing("fleet", [], async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).WaitAsync(Patience, CancellationToken.None);

            return McpResult.Ok("late");
        });

        await new StdioMcpServer(new StringReader(transcript), output, _log, clock)
            .RunAsync(serving)
            .WaitAsync(Patience);

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(0, clock.ActiveTimers);
        Assert.Empty(output.Lines);
    }

    private static string Call(int id, string meta = "") =>
        $"{{\"id\":{id},\"method\":\"tools/call\",\"params\":{{\"name\":\"list_agents\",\"arguments\":{{}}{meta}}}}}";

    private static McpServing Blocking(Task release, TaskCompletionSource? started = null) => new("fleet", [], async (_, _) =>
    {
        started?.SetResult();

        await release.WaitAsync(Patience);

        return McpResult.Ok("ok");
    });

    // Parsing fails the test when a line is not one whole JSON message.
    private static string IdOf(string line)
    {
        using var message = JsonDocument.Parse(line);

        return message.RootElement.TryGetProperty("id", out var id) ? id.GetRawText() : string.Empty;
    }

    private sealed class FakeLog : IFleetLog
    {
        public List<Exception> Swallows { get; } = [];

        public void Swallowed(Exception e) => Swallows.Add(e);

        public void Write(string line) { }

        public IReadOnlyList<string> Tail(int lines) => [];
    }
}
