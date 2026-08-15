namespace Fleet.Features.Mcp.SyncClaudeConfig.Models;

public sealed record ClaudePermissions(
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny,
    IReadOnlyList<string> Ask);
