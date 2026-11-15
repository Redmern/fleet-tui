using Fleet.Features.Agents.TellAgent;
using Fleet.Ports.Agents;
using Fleet.Shared.Constants;
using Fleet.Shared.Mcp;
using Fleet.Shared.Messaging;

namespace Fleet.Cli.Composition;

public sealed class CallerHarnesses(string project, string caller, IAgentStore store)
{
    public const string MainHarness = AgentHarness.Claude;

    public string Current()
    {
        var id = caller.Trim();

        if (id.Length == 0)
        {
            return MainHarness;
        }

        var isAgent = id.StartsWith(McpCaller.AgentPrefix, StringComparison.Ordinal);
        var key = isAgent ? id[McpCaller.AgentPrefix.Length..] : id;
        var record = store.List(project).FirstOrDefault(a => isAgent
            ? !AgentHarness.IsOrchestrator(a.Harness)
              && string.Equals($"{a.Repository}/{a.Branch}", key, StringComparison.OrdinalIgnoreCase)
            : AgentHarness.IsOrchestrator(a.Harness)
              && string.Equals(a.Branch, key, StringComparison.OrdinalIgnoreCase));

        return record is null ? HarnessRoute.Unknown : TellAgentHandler.HarnessOf(record);
    }
}
