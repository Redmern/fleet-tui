using Fleet.Shared.Hooks;
using Fleet.Shared.Status.Enums;

namespace Fleet.Features.Agents.ListAgents;

public static class AgentActivity
{
    public const string Working = "working";

    public const string Waiting = "waiting";

    public const string Idle = "idle";

    public const string Stalled = "stalled";

    public static AgentState Confirmed(AgentState state, string paneText, string reason = "") => state switch
    {
        AgentState.Blocked when reason == HookStatus.PermissionReason
            && Classify(paneText) == Idle && !Prompting(paneText) => AgentState.Idle,
        AgentState.Stalled when Prompting(paneText) => AgentState.Blocked,
        AgentState.Stalled when Classify(paneText) != Working => AgentState.Idle,
        AgentState.Blocked when Classify(paneText) == Working && !Prompting(paneText) => AgentState.Working,
        _ => state,
    };

    public static bool NeedsPane(AgentState state) => state is AgentState.Stalled or AgentState.Blocked;

    private static bool Prompting(string paneText)
    {
        var text = paneText.ToLowerInvariant();

        return text.Contains("do you want")
            || text.Contains("waiting for your input")
            || text.Contains("no, and tell claude");
    }

    public static string For(AgentState state) => state switch
    {
        AgentState.Blocked => Waiting,
        AgentState.Stalled => Stalled,
        AgentState.Working => Working,
        AgentState.Idle => Idle,
        _ => string.Empty,
    };

    public static string Classify(string paneText)
    {
        if (paneText.Trim().Length == 0)
        {
            return string.Empty;
        }

        var text = paneText.ToLowerInvariant();

        if (text.Contains("esc to interrupt"))
        {
            return Working;
        }

        if (text.Contains("do you want")
            || text.Contains("waiting for your input")
            || text.Contains("no, and tell claude"))
        {
            return Waiting;
        }

        return Idle;
    }
}
