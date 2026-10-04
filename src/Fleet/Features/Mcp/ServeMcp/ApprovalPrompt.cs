using Fleet.Ports.Mcp.Models;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Mcp.ServeMcp;

public static class ApprovalPrompt
{
    public static string For(HarnessTool tool, McpRequest request, string caller)
    {
        var who = caller.Trim().Length == 0 ? "the main orchestrator" : SafeText.Clean(caller, 60);

        var target = Target(request);

        var action = SettingsDefaults.Describe(tool);

        return target.Length == 0
            ? $"{who} wants to: {action}"
            : $"{who} wants to: {action} — {target}";
    }

    private static string Target(McpRequest request)
    {
        var repository = SafeText.Clean(request.Value(ToolArguments.Repository), 60);
        var branch = SafeText.Clean(request.Value(ToolArguments.Branch), 60);
        var message = SafeText.Clean(request.Value(ToolArguments.Message));
        var slug = SafeText.Clean(request.Value(ToolArguments.Slug), 60);

        if (repository.Length > 0 && branch.Length > 0)
        {
            return $"{repository}/{branch}";
        }

        if (repository.Length > 0)
        {
            return repository;
        }

        return slug.Length > 0 ? slug + Extent(request) : message;
    }

    private static string Extent(McpRequest request)
    {
        var also = new List<string>();

        if (request.Flag(ToolArguments.DeleteFolder))
        {
            also.Add("deleting its folder");
        }

        if (request.Flag(ToolArguments.RemoveAgents))
        {
            also.Add("removing its agents");
        }

        return also.Count == 0 ? string.Empty : $" ({string.Join(", ", also)})";
    }
}
