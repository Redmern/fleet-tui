using Fleet.Platform.Approvals;
using Fleet.Ports.Approvals.Enums;
using Fleet.Ports.Approvals.Models;

namespace Fleet.Tests.Platform.Approvals;

[Collection(ConfigHomeCollection.Name)]
public sealed class FileApprovalChannelTests : ConfigHomeFixture
{
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

        Assert.True((await asking).Allowed);
    }

    [Fact]
    public async Task A_dashboard_that_declines_returns_a_denial()
    {
        var channel = Channel();
        channel.Heartbeat("techweb");

        var asking = channel.AskAsync(Ask());

        await Answer(channel, ApprovalDecision.Denied);

        var outcome = await asking;

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
