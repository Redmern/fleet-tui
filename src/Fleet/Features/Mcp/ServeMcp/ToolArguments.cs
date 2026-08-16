using Fleet.Ports.Mcp.Models;

namespace Fleet.Features.Mcp.ServeMcp;

public static class ToolArguments
{
    public const string Repository = "repository";

    public const string Branch = "branch";

    public const string Message = "message";

    public const string Task = "task";

    public const string Status = "status";

    public const string Summary = "summary";

    public const string Harness = "harness";

    public const string DefaultBranch = "default_branch";

    public const string DeleteWorktree = "delete_worktree";

    public const string Visible = "visible";

    public const string Lines = "lines";

    public static string Text(McpRequest request, string key) => request.Value(key).Trim();

    public static bool Flag(McpRequest request, string key) => request.Flag(key);

    public static int Count(McpRequest request, string key, int fallback)
    {
        var raw = request.Value(key).Trim();

        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    public static string? Missing(McpRequest request, params string[] required)
    {
        foreach (var key in required)
        {
            if (request.Value(key).Trim().Length == 0)
            {
                return $"'{key}' is required for {request.Tool}.";
            }
        }

        return null;
    }
}
