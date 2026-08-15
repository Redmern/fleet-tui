namespace Fleet.Ports.Claude.Models;

public sealed record ClaudeState(
    bool ServerRegistered,
    bool ServerEnabled,
    IReadOnlyList<string> Allow)
{
    public static readonly ClaudeState Absent = new(false, false, []);
}
