using Fleet.Ports.Agents.Models;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ports.Notifications.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Hooks;
using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Features.Notifications.DetectNotices;

public sealed record AgentWatch(
    AgentRecord Agent,
    bool PaneAlive,
    string PaneText,
    TimeSpan Unchanged,
    int Behind,
    bool Conflicts,
    bool PaneLost = false,
    AgentReport? Hooked = null);

public static class NoticeDetector
{
    public static readonly TimeSpan StallAfter = TimeSpan.FromMinutes(10);

    public const int FarBehind = 20;

    public static IReadOnlyList<Notice> Detect(string project, IReadOnlyList<AgentWatch> agents, DateTime now)
    {
        var found = new List<Notice>();

        foreach (var watch in agents)
        {
            var agent = watch.Agent;
            var label = $"{agent.Repository} / {agent.Branch}";

            Notice Raise(NoticeKind kind, string message) =>
                new(project, kind, agent.Worktree, label, message, now);

            var reported = OrchestrationStatus.Normalize(agent.Status);
            var text = watch.PaneText.ToLowerInvariant();

            if (agent.Status.Length > 0 && reported == OrchestrationStatus.Failed)
            {
                found.Add(Raise(NoticeKind.Failed, "reported that it failed"));
            }
            else if (agent.Open && watch.PaneLost)
            {
                found.Add(Raise(NoticeKind.Failed, "its pane is gone"));
            }
            else if (agent.Status.Length > 0 && reported == OrchestrationStatus.Done)
            {
                found.Add(Raise(NoticeKind.Done, "is done and ready for review"));
            }

            if (watch.Hooked is { } hooked)
            {
                if (watch.PaneAlive && FromHook(hooked, now) is { } notice)
                {
                    found.Add(Raise(notice.Kind, notice.Message));
                }
            }
            else if (watch.PaneAlive && IsPermission(text))
            {
                found.Add(Raise(NoticeKind.Permission, "asks for permission"));
            }
            else if (watch.PaneAlive && IsQuestion(text))
            {
                found.Add(Raise(NoticeKind.NeedsInput, "has a question for you"));
            }
            else if (watch.PaneAlive && text.Contains("esc to interrupt", StringComparison.Ordinal) && watch.Unchanged >= StallAfter)
            {
                found.Add(Raise(NoticeKind.Stalled, $"has shown no progress for {(int)watch.Unchanged.TotalMinutes} min"));
            }

            if (watch.Conflicts)
            {
                found.Add(Raise(NoticeKind.BranchTrouble, $"conflicts with {agent.BaseRef}"));
            }
            else if (watch.Behind >= FarBehind)
            {
                found.Add(Raise(NoticeKind.BranchTrouble, $"is {watch.Behind} commits behind {agent.BaseRef}"));
            }
        }

        return found;
    }

    private static (NoticeKind Kind, string Message)? FromHook(AgentReport hooked, DateTime now) => hooked.State switch
    {
        AgentState.Blocked when hooked.Reason == HookStatus.PermissionReason => (NoticeKind.Permission, "asks for permission"),
        AgentState.Blocked => (NoticeKind.NeedsInput, "has a question for you"),
        AgentState.Stalled => (NoticeKind.Stalled, $"has reported no progress for {(int)(now - hooked.At).TotalMinutes} min"),
        _ => null,
    };

    public static bool IsPermission(string text) =>
        text.Contains("no, and tell claude", StringComparison.OrdinalIgnoreCase)
        || text.Contains("do you want to proceed", StringComparison.OrdinalIgnoreCase);

    public static bool IsQuestion(string text) =>
        text.Contains("do you want", StringComparison.OrdinalIgnoreCase)
        || text.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase);

    public static string Settled(string paneText) =>
        string.Join('\n', paneText
            .Split('\n')
            .Where(l => !l.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase))
            .Select(l => new string([.. l.Where(c => !char.IsDigit(c))]).TrimEnd()));
}
