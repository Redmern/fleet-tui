using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Shared.Hooks;

public sealed record HookEvent(
    string Name,
    string Cwd,
    string Session = "",
    string Transcript = "",
    string AgentId = "",
    string NotificationType = "",
    string Source = "",
    string Inbox = "",
    string Reason = "")
{
    public bool FromSubagent => AgentId.Length > 0;
}

public static class HookStatus
{
    public const string Verb = "hook";

    public const string PermissionReason = "permission";

    public const string InputReason = "input";

    public const string CompactSource = "compact";

    public const string SessionStartEvent = "SessionStart";

    public const string SessionEndEvent = "SessionEnd";

    public static IReadOnlyList<string> Events { get; } =
    [
        "UserPromptSubmit",
        "PreToolUse",
        "PostToolUse",
        "Stop",
        "SessionStart",
        "SessionEnd",
        "PermissionRequest",
        "Notification",
    ];

    public static readonly IReadOnlyList<string> ContinuingReasons = ["clear", "resume"];

    public static bool EndsForGood(HookEvent hook) =>
        hook.Name == SessionEndEvent
        && !hook.FromSubagent
        && !ContinuingReasons.Contains(hook.Reason, StringComparer.Ordinal);

    public static AgentReport? ReportFor(HookEvent hook, DateTime now)
    {
        if (hook.Cwd.Trim().Length == 0)
        {
            return null;
        }

        var outcome = StateFor(hook);

        if (outcome is not { } found || (hook.FromSubagent && Ends(found.State)))
        {
            return null;
        }

        return new AgentReport(
            hook.Cwd, hook.Session, found.State, now, hook.Transcript, found.Reason, StartsSession: hook.Name == "SessionStart",
            Inbox: hook.Inbox);
    }

    private static bool Ends(AgentState state) => state is AgentState.Idle or AgentState.Unknown;

    private static (AgentState State, string Reason)? StateFor(HookEvent hook) => hook.Name switch
    {
        "UserPromptSubmit" or "PreToolUse" or "PostToolUse" => (AgentState.Working, string.Empty),
        "SessionStart" when hook.Source == CompactSource => null,
        "Stop" or "SessionStart" => (AgentState.Idle, string.Empty),
        "SessionEnd" => (AgentState.Unknown, string.Empty),
        "PermissionRequest" => (AgentState.Blocked, PermissionReason),
        "Notification" => NotificationState(hook.NotificationType),
        _ => null,
    };

    private static (AgentState State, string Reason)? NotificationState(string type) => type switch
    {
        "permission_prompt" => (AgentState.Blocked, PermissionReason),
        "idle_prompt" => (AgentState.Idle, string.Empty),
        "auth_success" => null,
        _ => (AgentState.Blocked, InputReason),
    };
}
