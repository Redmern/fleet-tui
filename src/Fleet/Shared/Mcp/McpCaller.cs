namespace Fleet.Shared.Mcp;

public sealed record McpCaller(string Project, string Root, string Caller)
{
    public const string AgentPrefix = "agent:";

    public bool IsAgent => Caller.StartsWith(AgentPrefix, StringComparison.Ordinal);

    public bool IsSub => Caller.Trim().Length > 0 && !IsAgent;

    public string AgentId => IsAgent ? Caller[AgentPrefix.Length..] : string.Empty;

    public static McpCaller AtRoot(string project, string root) =>
        new(project, root, string.Empty);

    public static string ForAgent(string repository, string branch) =>
        $"{AgentPrefix}{repository}/{branch}";
}
