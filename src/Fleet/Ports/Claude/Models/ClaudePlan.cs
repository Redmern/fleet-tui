namespace Fleet.Ports.Claude.Models;

public sealed record ClaudePlan(
    string Directory,
    McpServerEntry Server,
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny,
    IReadOnlyList<string> Ask,
    IReadOnlyList<string> EnabledServers,
    string HookCommand,
    IReadOnlyList<string> HookArgs);
