using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ports.Notifications.Models;

namespace Fleet.Features.Notifications.ShowNotices;

public static class ApprovalNotices
{
    public const string RowPrefix = "approval:";

    public static string? Hint(string worktree, IReadOnlyList<PendingApproval> pending)
    {
        if (!worktree.StartsWith(RowPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var id = worktree[RowPrefix.Length..];

        return pending.FirstOrDefault(p => p.Id == id) is { } waiting
            ? $"{waiting.Request.CallerName} waits to {waiting.Request.Tool} in {waiting.Request.Project}: answer in the approve? popup over its pane, or wait for the dashboard to ask"
            : "that approval is no longer waiting";
    }

    public static IReadOnlyList<Notice> For(IReadOnlyList<PendingApproval> pending) =>
        [.. pending.Select(p => new Notice(
            p.Request.Project,
            NoticeKind.Approval,
            RowPrefix + p.Id,
            p.Request.CallerName,
            $"needs approval to {p.Request.Tool} in {p.Request.Project}",
            p.Asked.UtcDateTime))];
}
