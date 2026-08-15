using Fleet.Ports.Claude.Models;
using Fleet.Shared.Mcp;

namespace Fleet.Features.Mcp.SyncClaudeConfig;

public static class McpRegistration
{
    public static McpServerEntry For(string executable, string project, string caller)
    {
        var args = new List<string> { "mcp", "--project", project };

        if (caller.Trim().Length > 0)
        {
            args.Add("--caller");
            args.Add(caller.Trim());
        }

        return new McpServerEntry(McpServerId.ServerName, executable, args);
    }
}
