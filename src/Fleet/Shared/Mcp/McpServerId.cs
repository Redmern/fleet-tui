using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Shared.Mcp;

public static class McpServerId
{
    public const string ServerName = "fleet";

    public static string RuleId(HarnessTool tool) =>
        $"mcp__{ServerName}__{HarnessToolIds.For(tool)}";
}
