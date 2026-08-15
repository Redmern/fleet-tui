namespace Fleet.Shared.Mcp;

public sealed record McpCaller(string Project, string Root, string Caller)
{
    public bool IsSub => Caller.Trim().Length > 0;

    public static McpCaller AtRoot(string project, string root) =>
        new(project, root, string.Empty);
}
