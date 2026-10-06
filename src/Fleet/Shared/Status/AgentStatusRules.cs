using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Shared.Status;

public static class AgentStatusRules
{
    public static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(600);

    public static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(24);

    public static int Severity(AgentState state) => (int)state;

    public static AgentState Derive(AgentReport report, DateTime now, TimeSpan stallAfter) =>
        report.State == AgentState.Working && now - report.At >= stallAfter
            ? AgentState.Stalled
            : report.State;

    public static IComparer<AgentReport> MoreUrgent(DateTime now, TimeSpan stallAfter) =>
        Comparer<AgentReport>.Create((left, right) =>
        {
            var bySeverity = Severity(Derive(right, now, stallAfter))
                .CompareTo(Severity(Derive(left, now, stallAfter)));

            return bySeverity != 0 ? bySeverity : left.At.CompareTo(right.At);
        });

    public static AgentReport? Aggregate(IEnumerable<AgentReport> reports, DateTime now, TimeSpan stallAfter) =>
        reports
            .Where(r => r.State != AgentState.Unknown)
            .Order(MoreUrgent(now, stallAfter))
            .Select(r => r with { State = Derive(r, now, stallAfter) })
            .FirstOrDefault();

    public static AgentReport? For(AgentSnapshot snapshot, string worktree, DateTime now, TimeSpan stallAfter) =>
        worktree.Length == 0
            ? null
            : Aggregate(snapshot.Reports.Where(r => PathKey.Within(r.Worktree, worktree)), now, stallAfter);

    public static string? InboxOf(AgentSnapshot snapshot, string folder) =>
        folder.Length == 0
            ? null
            : snapshot.Reports
                .Where(r => r.Inbox.Length > 0 && PathKey.Same(r.Worktree, folder))
                .OrderByDescending(r => r.At)
                .Select(r => r.Inbox)
                .FirstOrDefault();

    public static bool Forgotten(AgentReport report, DateTime now) => now - report.At >= ForgetAfter;
}
