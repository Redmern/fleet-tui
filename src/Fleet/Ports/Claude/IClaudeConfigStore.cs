using Fleet.Ports.Claude.Models;
using Fleet.Shared.Results;

namespace Fleet.Ports.Claude;

public interface IClaudeConfigStore
{
    Result Sync(ClaudePlan plan);

    Result EnableServer(string userSettingsPath, string serverName);

    Result SyncWorktree(McpServerEntry server, string directory, IReadOnlyList<string> allow);

    ClaudeState Inspect(string directory, string serverName);
}
