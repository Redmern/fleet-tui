namespace Fleet.Ports.Claude.Models;

public sealed record ClaudeState(
    bool ServerRegistered,
    bool ServerEnabled,
    bool HookInstalled,
    IReadOnlyList<string> Allow,
    bool StatusHooksInstalled = false)
{
    public static readonly ClaudeState Absent = new(false, false, false, []);
}
