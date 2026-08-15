using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Mcp.ServeMcp;

public static class McpAudit
{
    private static string Who(string caller) =>
        caller.Trim().Length == 0 ? "mcp main" : $"mcp {SafeText.Clean(caller, 60)}";

    public static string Requested(string caller, HarnessTool tool) =>
        $"{Who(caller)}: {HarnessToolIds.For(tool)} requested";

    public static string Allowed(string caller, HarnessTool tool) =>
        $"{Who(caller)}: {HarnessToolIds.For(tool)} allowed";

    public static string Denied(string caller, HarnessTool tool, string reason) =>
        $"{Who(caller)}: {HarnessToolIds.For(tool)} denied — {SafeText.Clean(reason)}";

    public static string Forbidden(string caller, HarnessTool tool) =>
        $"{Who(caller)}: {HarnessToolIds.For(tool)} is not allowed for this project";
}
