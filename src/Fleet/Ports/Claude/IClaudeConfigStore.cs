using Fleet.Ports.Claude.Models;
using Fleet.Shared.Results;

namespace Fleet.Ports.Claude;

public interface IClaudeConfigStore
{
    Result Sync(ClaudePlan plan);

    Result EnableServer(string userSettingsPath, string serverName);

    ClaudeState Inspect(string directory, string serverName);
}
