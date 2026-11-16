using Fleet.Cli.Composition;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ports.Notifications.Models;

namespace Fleet.Tests.Cli;

public sealed class DashboardSkeletonApprovalTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly List<(string Title, string Body)> _toasts = [];

    private static PendingApproval Pending(string id = "a1") =>
        new(id, new ApprovalRequest("techweb", "new_agent", "wants to start", Caller: "agent:backend/login"), Now);

    private void Toast(IReadOnlyList<string> queued, bool on) =>
        DashboardSkeleton.ToastQueued(() => queued, () => on, (title, body) => _toasts.Add((title, body)), work => work());

    [Fact]
    public void Every_queued_line_becomes_a_toast_when_toasts_are_on()
    {
        Toast(["approval needed in techweb: new_agent by upgrade", "backend/login: done"], on: true);

        Assert.Equal(
            [("fleet", "approval needed in techweb: new_agent by upgrade"), ("fleet", "backend/login: done")],
            _toasts);
    }

    [Fact]
    public void Queued_lines_make_no_toast_when_the_toast_setting_is_off()
    {
        Toast(["backend/login: done"], on: false);

        Assert.Empty(_toasts);
    }

    [Fact]
    public void An_empty_queue_does_not_even_read_the_setting()
    {
        var asked = false;

        DashboardSkeleton.ToastQueued(
            () => [],
            () =>
            {
                asked = true;
                return true;
            },
            (_, _) => { },
            work => work());

        Assert.False(asked);
    }

    [Fact]
    public void The_toasts_are_handed_to_the_runner_instead_of_being_shown_on_the_calling_thread()
    {
        Action? handed = null;

        DashboardSkeleton.ToastQueued(
            () => ["backend/login: done"],
            () => true,
            (title, body) => _toasts.Add((title, body)),
            work => handed = work);

        Assert.Empty(_toasts);
        handed!();
        Assert.Single(_toasts);
    }

    [Fact]
    public void A_pending_approval_is_a_row_ahead_of_the_stored_notices_and_counts_as_open()
    {
        var stored = new Notice("techweb", NoticeKind.Done, "/work/x", "api / x", "done", Now);

        var board = DashboardSkeleton.Notices([stored], [Pending()], Now);

        Assert.Equal(2, board.Rows.Count);
        Assert.Equal(2, board.Open);
        Assert.StartsWith($"{NoticeKind.Approval}|", board.KeyAt(0));
        Assert.Equal(stored.Key, board.KeyAt(1));
    }

    [Fact]
    public void Without_a_pending_approval_the_board_is_just_the_stored_notices()
    {
        var stored = new Notice("techweb", NoticeKind.Done, "/work/x", "api / x", "done", Now);

        var board = DashboardSkeleton.Notices([stored], [], Now);

        Assert.Equal(1, board.Open);
        Assert.Equal(stored.Key, board.KeyAt(0));
    }
}
