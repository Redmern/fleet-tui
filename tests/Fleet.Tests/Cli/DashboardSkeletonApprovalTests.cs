using Fleet.Cli.Composition;

namespace Fleet.Tests.Cli;

public sealed class DashboardSkeletonApprovalTests
{
    private readonly List<(string Title, string Body)> _toasts = [];

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
}
