using Fleet.Shared.Status.Enums;

namespace Fleet.Shared.Status.Models;

public sealed record AgentReport(
    string Worktree,
    string Session,
    AgentState State,
    DateTime At,
    string Transcript = "",
    string Reason = "",
    bool StartsSession = false);
