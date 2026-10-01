using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Shared.Hooks;

public sealed record HookEvent(
    string Name,
    string Cwd,
    string Session = "",
    string Transcript = "",
    string AgentId = "",
    string NotificationType = "")
{
    public bool FromSubagent => AgentId.Length > 0;
}

public static class HookStatus
{
    public const string Verb = "hook";

    public const string PermissionReason = "permission";

    public const string InputReason = "input";

    public static IReadOnlyList<string> Events { get; } =
    [
        "UserPromptSubmit",
        "PreToolUse",
        "Stop",
        "SessionStart",
        "SessionEnd",
        "PermissionRequest",
        "Notification",
    ];

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

        return new AgentReport(hook.Cwd, hook.Session, found.State, now, hook.Transcript, found.Reason);
    }

    private static bool Ends(AgentState state) => state is AgentState.Idle or AgentState.Unknown;

    private static (AgentState State, string Reason)? StateFor(HookEvent hook) => hook.Name switch
    {
        "UserPromptSubmit" or "PreToolUse" => (AgentState.Working, string.Empty),
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
