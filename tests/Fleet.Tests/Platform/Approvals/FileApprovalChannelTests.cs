using Fleet.Platform.Approvals;
using Fleet.Platform.Storage;
using Fleet.Ports.Approvals.Enums;
using Fleet.Ports.Approvals.Models;

namespace Fleet.Tests.Platform.Approvals;

public sealed class FileApprovalChannelTests : ConfigHomeFixture
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private DateTimeOffset _now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static ApprovalRequest Ask(string project = "techweb") =>
        new(project, "new_agent", "upgrade wants to start backend/login");

    private FileApprovalChannel Channel(TimeSpan? timeout = null, bool realClock = false) => new(
        poll: TimeSpan.FromMilliseconds(15),
        timeout: timeout ?? TimeSpan.FromSeconds(5),
        stale: TimeSpan.FromSeconds(12),
        now: realClock ? null : () => _now);

    [Fact]
    public async Task Without_a_heartbeat_the_request_fails_fast_as_no_dashboard()
    {
        var outcome = await Channel().AskAsync(Ask());

        Assert.Equal(ApprovalDecision.NoDashboard, outcome.Decision);
    }

    [Fact]
    public async Task A_dashboard_that_allows_the_request_lets_it_through()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        var asking = channel.AskAsync(Ask());

        await Answer(channel, ApprovalDecision.Allowed);

        Assert.True((await asking.WaitAsync(Patience)).Allowed);
    }

    [Fact]
    public async Task A_dashboard_that_declines_returns_a_denial()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        var asking = channel.AskAsync(Ask());

        await Answer(channel, ApprovalDecision.Denied);

        var outcome = await asking.WaitAsync(Patience);

        Assert.Equal(ApprovalDecision.Denied, outcome.Decision);
        Assert.False(outcome.Allowed);
    }

    [Fact]
    public async Task An_unanswered_request_expires_rather_than_hanging_forever()
    {
        var channel = Channel(timeout: TimeSpan.FromMilliseconds(200), realClock: true);
        channel.Heartbeat("techweb");

        var outcome = await channel.AskAsync(Ask());

        Assert.Equal(ApprovalDecision.Expired, outcome.Decision);
    }

    [Fact]
    public void The_pending_request_carries_the_tool_and_summary_to_the_dashboard()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask());

        var pending = SpinForPending(channel, "techweb");

        Assert.NotNull(pending);
        Assert.Equal("new_agent", pending!.Request.Tool);
        Assert.Contains("backend/login", pending.Request.Summary);
    }

    [Fact]
    public void A_request_is_taken_only_once_so_two_polls_do_not_double_prompt()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask());

        Assert.NotNull(SpinForPending(channel, "techweb"));
        Assert.Null(channel.TakePending("techweb"));
    }

    [Fact]
    public async Task A_stale_heartbeat_reads_as_no_dashboard()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        _now = _now.AddSeconds(30);

        var outcome = await channel.AskAsync(Ask());

        Assert.Equal(ApprovalDecision.NoDashboard, outcome.Decision);
    }

    [Fact]
    public async Task Retiring_the_dashboard_stops_answering_requests()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");
        channel.Retire("techweb");

        var outcome = await channel.AskAsync(Ask());

        Assert.Equal(ApprovalDecision.NoDashboard, outcome.Decision);
    }

    [Fact]
    public void One_projects_requests_never_surface_on_another()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");
        channel.Heartbeat("other");

        _ = channel.AskAsync(Ask("techweb"));

        Assert.NotNull(SpinForPending(channel, "techweb"));
        Assert.Null(channel.TakePending("other"));
    }

    [Fact]
    public void A_request_from_a_pane_is_taken_only_by_that_panes_float()
    {
        var channel = Channel(TimeSpan.FromSeconds(60));
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask() with { Pane = "p7" });

        Assert.Null(channel.TakePending("techweb", "p9"));
        Assert.Null(channel.TakePending("techweb"));
        Assert.Equal("p7", channel.TakePending("techweb", "p7")!.Request.Pane);
    }

    [Fact]
    public void The_dashboard_takes_a_panes_request_when_no_float_took_it_in_time()
    {
        var channel = Channel(TimeSpan.FromSeconds(60));
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask() with { Pane = "p7" });
        Assert.Null(channel.TakePending("techweb"));

        _now += TimeSpan.FromSeconds(6);

        Assert.NotNull(channel.TakePending("techweb"));
    }

    [Fact]
    public async Task An_expired_request_names_who_it_was_shown_over_and_how_long_it_waited()
    {
        var channel = Channel(timeout: TimeSpan.FromSeconds(90));
        channel.Heartbeat("techweb");

        var asking = channel.AskAsync(Ask() with { Caller = "upgrade" });
        SpinForPending(channel, "techweb");

        _now += TimeSpan.FromSeconds(91);
        var outcome = await asking.WaitAsync(Patience);

        Assert.Equal(ApprovalDecision.Expired, outcome.Decision);
        Assert.Equal(
            "No one answered the approval prompt (shown over upgrade in techweb) within 90 s",
            outcome.Reason);
        Assert.DoesNotContain("dashboard", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_expired_request_from_the_main_pane_says_it_was_shown_over_the_orchestrator()
    {
        var channel = Channel(timeout: TimeSpan.FromSeconds(90));
        channel.Heartbeat("techweb");

        var asking = channel.AskAsync(Ask());
        SpinForPending(channel, "techweb");

        _now += TimeSpan.FromSeconds(91);

        Assert.Equal(
            "No one answered the approval prompt (shown over orchestrator in techweb) within 90 s",
            (await asking.WaitAsync(Patience)).Reason);
    }

    [Fact]
    public async Task A_request_cannot_wait_past_its_own_timeout()
    {
        var channel = Channel(timeout: TimeSpan.FromSeconds(120));
        channel.Heartbeat("techweb");

        var asking = channel.AskAsync(Ask() with { Caller = "upgrade", Timeout = TimeSpan.FromSeconds(55) });
        SpinForPending(channel, "techweb");

        _now += TimeSpan.FromSeconds(56);
        var outcome = await asking.WaitAsync(Patience);

        Assert.Equal(ApprovalDecision.Expired, outcome.Decision);
        Assert.EndsWith("within 55 s", outcome.Reason);
    }

    [Fact]
    public async Task A_request_that_arrived_earlier_has_only_the_time_that_is_left()
    {
        var channel = Channel(timeout: TimeSpan.FromSeconds(120));
        channel.Heartbeat("techweb");

        var asking = channel.AskAsync(Ask() with
        {
            Timeout = TimeSpan.FromSeconds(55),
            Arrived = _now - TimeSpan.FromSeconds(50),
        });
        SpinForPending(channel, "techweb");

        _now += TimeSpan.FromSeconds(6);
        var outcome = await asking.WaitAsync(Patience);

        Assert.Equal(ApprovalDecision.Expired, outcome.Decision);
        Assert.EndsWith("within 55 s", outcome.Reason);
    }

    [Fact]
    public async Task An_answer_after_the_deadline_leaves_no_reply_behind()
    {
        var channel = Channel(timeout: TimeSpan.FromSeconds(55));
        channel.Heartbeat("techweb");

        var asking = channel.AskAsync(Ask());
        var pending = SpinForPending(channel, "techweb");

        _now += TimeSpan.FromSeconds(56);
        var outcome = await asking.WaitAsync(Patience);

        channel.Answer("techweb", pending!.Id, ApprovalDecision.Allowed);

        Assert.False(outcome.Allowed);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(FleetPaths.Approvals, "techweb")));
    }

    [Fact]
    public void The_pending_request_carries_the_caller_to_the_dashboard()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask() with { Caller = "upgrade" });

        Assert.Equal("upgrade", SpinForPending(channel, "techweb")!.Request.Caller);
    }

    [Fact]
    public void Peeking_lists_a_waiting_request_without_taking_it()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask() with { Caller = "upgrade" });

        var first = Assert.Single(channel.Pending("techweb"));
        Assert.Equal("upgrade", first.Request.Caller);
        Assert.Equal("new_agent", first.Request.Tool);
        Assert.Equal(_now, first.Asked);
        Assert.Single(channel.Pending("techweb"));
        Assert.NotNull(channel.TakePending("techweb"));
    }

    [Fact]
    public void Peeking_still_lists_a_request_the_dashboard_has_taken_until_it_is_answered()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask());
        var taken = SpinForPending(channel, "techweb");

        Assert.Single(channel.Pending("techweb"));

        channel.Answer("techweb", taken!.Id, ApprovalDecision.Allowed);

        Assert.Empty(channel.Pending("techweb"));
    }

    [Fact]
    public async Task Peeking_stops_listing_a_request_once_it_expired()
    {
        var channel = Channel(timeout: TimeSpan.FromSeconds(55));
        channel.Heartbeat("techweb");

        var asking = channel.AskAsync(Ask());
        Assert.Single(channel.Pending("techweb"));

        _now += TimeSpan.FromSeconds(56);
        await asking.WaitAsync(Patience);

        Assert.Empty(channel.Pending("techweb"));
    }

    [Fact]
    public void Peeking_ignores_a_request_left_behind_by_a_dead_caller()
    {
        var channel = Channel(timeout: TimeSpan.FromSeconds(55));
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask());
        _now += TimeSpan.FromMinutes(10);

        Assert.Empty(channel.Pending("techweb"));
    }

    [Fact]
    public void Peeking_stops_listing_a_request_at_its_own_timeout_not_the_channels()
    {
        var channel = Channel(timeout: TimeSpan.FromSeconds(120));
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask() with { Timeout = TimeSpan.FromSeconds(55) });
        Assert.Single(channel.Pending("techweb"));

        _now += TimeSpan.FromSeconds(56);

        Assert.Empty(channel.Pending("techweb"));
    }

    [Fact]
    public void Peeking_skips_a_request_file_it_cannot_read()
    {
        var channel = Channel();
        var dir = Path.Combine(FleetPaths.Approvals, "techweb");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{_now.UtcTicks:D19}-broken.ask"), "not json");

        Assert.Empty(channel.Pending("techweb"));
    }

    [Fact]
    public void Peeking_lists_a_request_once_even_when_it_is_taken_while_listed()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");
        _ = channel.AskAsync(Ask());
        var taken = SpinForPending(channel, "techweb");
        var dir = Path.Combine(FleetPaths.Approvals, "techweb");
        File.WriteAllText(Path.Combine(dir, taken!.Id + ".ask"), File.ReadAllText(Path.Combine(dir, taken.Id + ".taken")));

        Assert.Single(channel.Pending("techweb"));
    }

    [Fact]
    public void Peeking_never_lists_another_projects_requests()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        _ = channel.AskAsync(Ask());

        Assert.Empty(channel.Pending("other"));
    }

    private async Task Answer(FileApprovalChannel channel, ApprovalDecision decision)
    {
        var pending = SpinForPending(channel, "techweb");

        Assert.NotNull(pending);

        channel.Answer("techweb", pending!.Id, decision);

        await Task.Yield();
    }

    private static PendingApproval? SpinForPending(FileApprovalChannel channel, string project)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var pending = channel.TakePending(project);

            if (pending is not null)
            {
                return pending;
            }

            Thread.Sleep(5);
        }

        return null;
    }
}
