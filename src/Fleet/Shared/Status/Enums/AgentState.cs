namespace Fleet.Shared.Status.Enums;

public enum AgentState
{
    Unknown = -1,
    Idle = 0,
    Working = 1,
    Stalled = 2,
    Blocked = 3,
}
