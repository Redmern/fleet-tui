using Fleet.Shared.Status;
using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Tests.Shared;

public sealed class AgentStatusRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Stall = AgentStatusRules.StallAfter;

    private static AgentReport Report(AgentState state, TimeSpan ago, string session = "s1", string worktree = "C:/w/a") =>
        new(worktree, session, state, Now - ago);

    [Fact]
    public void The_stall_threshold_defaults_to_ten_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(600), AgentStatusRules.StallAfter);
    }

    [Theory]
    [InlineData(AgentState.Blocked, 3)]
    [InlineData(AgentState.Stalled, 2)]
    [InlineData(AgentState.Working, 1)]
    [InlineData(AgentState.Idle, 0)]
    [InlineData(AgentState.Unknown, -1)]
    public void Severity_follows_the_hook_contract(AgentState state, int severity)
    {
        Assert.Equal(severity, AgentStatusRules.Severity(state));
    }

    [Fact]
    public void Derive_keeps_recent_work_as_working()
    {
        Assert.Equal(AgentState.Working, AgentStatusRules.Derive(Report(AgentState.Working, TimeSpan.FromMinutes(9)), Now, Stall));
    }

    [Fact]
    public void Derive_presents_work_past_the_threshold_as_stalled()
    {
        Assert.Equal(AgentState.Stalled, AgentStatusRules.Derive(Report(AgentState.Working, TimeSpan.FromMinutes(10)), Now, Stall));
    }

    [Theory]
    [InlineData(AgentState.Idle)]
    [InlineData(AgentState.Blocked)]
    public void Derive_never_stalls_anything_but_work(AgentState state)
    {
        Assert.Equal(state, AgentStatusRules.Derive(Report(state, TimeSpan.FromHours(3)), Now, Stall));
    }

    [Fact]
    public void Aggregate_of_nothing_is_unknown()
    {
        Assert.Null(AgentStatusRules.Aggregate([], Now, Stall));
    }

    [Fact]
    public void Aggregate_takes_the_highest_severity_across_sessions()
    {
        var found = AgentStatusRules.Aggregate(
            [
                Report(AgentState.Working, TimeSpan.FromSeconds(5), "a"),
                Report(AgentState.Blocked, TimeSpan.FromSeconds(30), "b"),
                Report(AgentState.Idle, TimeSpan.FromSeconds(1), "c"),
            ],
            Now,
            Stall);

        Assert.Equal(AgentState.Blocked, found!.State);
        Assert.Equal("b", found.Session);
    }

    [Fact]
    public void Aggregate_ranks_a_derived_stall_above_fresh_work()
    {
        var found = AgentStatusRules.Aggregate(
            [
                Report(AgentState.Working, TimeSpan.FromSeconds(5), "fresh"),
                Report(AgentState.Working, TimeSpan.FromMinutes(20), "stuck"),
            ],
            Now,
            Stall);

        Assert.Equal(AgentState.Stalled, found!.State);
        Assert.Equal("stuck", found.Session);
    }

    [Fact]
    public void Aggregate_ignores_cleared_reports()
    {
        Assert.Null(AgentStatusRules.Aggregate([Report(AgentState.Unknown, TimeSpan.Zero)], Now, Stall));
    }

    [Fact]
    public void MoreUrgent_sorts_by_severity_then_oldest_first()
    {
        var sorted = new[]
            {
                Report(AgentState.Idle, TimeSpan.FromMinutes(1), "idle"),
                Report(AgentState.Blocked, TimeSpan.FromMinutes(1), "blocked-new"),
                Report(AgentState.Working, TimeSpan.FromMinutes(1), "working"),
                Report(AgentState.Blocked, TimeSpan.FromMinutes(5), "blocked-old"),
                Report(AgentState.Working, TimeSpan.FromMinutes(15), "stalled"),
            }
            .Order(AgentStatusRules.MoreUrgent(Now, Stall))
            .Select(r => r.Session)
            .ToList();

        Assert.Equal(["blocked-old", "blocked-new", "stalled", "working", "idle"], sorted);
    }

    [Fact]
    public void For_matches_reports_from_the_worktree_and_folders_inside_it()
    {
        var snapshot = new AgentSnapshot(
        [
            Report(AgentState.Working, TimeSpan.Zero, "inside", "C:/w/a/src"),
            Report(AgentState.Blocked, TimeSpan.Zero, "other", "C:/w/ab"),
        ]);

        var found = AgentStatusRules.For(snapshot, "C:/w/a", Now, Stall);

        Assert.Equal("inside", found!.Session);
    }

    [Fact]
    public void For_an_agent_with_no_reports_is_unknown()
    {
        Assert.Null(AgentStatusRules.For(AgentSnapshot.Empty, "C:/w/a", Now, Stall));
    }

    [Fact]
    public void Reports_older_than_a_day_are_forgotten()
    {
        Assert.False(AgentStatusRules.Forgotten(Report(AgentState.Idle, TimeSpan.FromHours(23)), Now));
        Assert.True(AgentStatusRules.Forgotten(Report(AgentState.Working, TimeSpan.FromHours(24)), Now));
    }

    [Fact]
    public void The_inbox_of_a_folder_is_the_newest_report_there_that_has_one()
    {
        var snapshot = new AgentSnapshot(
        [
            Report(AgentState.Idle, TimeSpan.FromMinutes(5), "old") with { Inbox = "uds:old" },
            Report(AgentState.Working, TimeSpan.FromMinutes(1), "new") with { Inbox = "uds:new" },
            Report(AgentState.Working, TimeSpan.Zero, "bare"),
            Report(AgentState.Idle, TimeSpan.Zero, "child", "C:/w/a/src") with { Inbox = "uds:child" },
        ]);

        Assert.Equal("uds:new", AgentStatusRules.InboxOf(snapshot, "C:/w/a/"));
        Assert.Null(AgentStatusRules.InboxOf(snapshot, "C:/w"));
        Assert.Null(AgentStatusRules.InboxOf(snapshot, string.Empty));
    }
}
