using Fleet.Features.Notifications.ShowNotices;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Notifications;

public sealed class ApprovalNoticesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static PendingApproval Pending(string id, string caller = "agent:backend/login") =>
        new(id, new ApprovalRequest("techweb", "new_agent", "wants to start backend/login", Caller: caller), At);

    [Fact]
    public void A_pending_approval_becomes_an_open_row_naming_the_caller_the_project_and_the_action()
    {
        var notice = Assert.Single(ApprovalNotices.For([Pending("a1")]));

        Assert.Equal(NoticeKind.Approval, notice.Kind);
        Assert.True(notice.IsOpen);
        Assert.Equal("techweb", notice.Project);
        Assert.Equal("agent:backend/login", notice.Agent);
        Assert.Contains("new_agent", notice.Message);
        Assert.Contains("techweb", notice.Message);
        Assert.Equal(At.UtcDateTime, notice.Since);
    }

    [Fact]
    public void Two_pending_approvals_make_two_rows_with_their_own_keys()
    {
        var notices = ApprovalNotices.For([Pending("a1"), Pending("a2", "agent:backend/pay")]);

        Assert.Equal(2, notices.Select(n => n.Key).Distinct().Count());
    }

    [Fact]
    public void A_caller_that_is_the_main_pane_is_named_as_such()
    {
        Assert.Equal("orchestrator", Assert.Single(ApprovalNotices.For([Pending("a1", caller: "")])).Agent);
    }

    [Fact]
    public void Opening_an_approval_row_says_where_to_answer_it()
    {
        var notice = Assert.Single(ApprovalNotices.For([Pending("a1")]));

        var hint = ApprovalNotices.Hint(notice.Worktree, [Pending("a1")]);

        Assert.Contains("agent:backend/login", hint);
        Assert.Contains("new_agent", hint);
        Assert.Contains("approve?", hint);
    }

    [Fact]
    public void Opening_a_row_whose_approval_was_answered_says_so()
    {
        var notice = Assert.Single(ApprovalNotices.For([Pending("a1")]));

        Assert.Equal("that approval is no longer waiting", ApprovalNotices.Hint(notice.Worktree, []));
    }

    [Fact]
    public void An_agent_row_is_not_an_approval_row()
    {
        Assert.Null(ApprovalNotices.Hint("/work/backend/login", [Pending("a1")]));
    }

    [Fact]
    public void Nothing_pending_means_no_rows()
    {
        Assert.Empty(ApprovalNotices.For([]));
    }

    [Fact]
    public void The_row_has_its_own_mark()
    {
        Assert.Equal(("▸", FleetTones.Warn), NoticeRows.Look(NoticeKind.Approval));
    }
}
