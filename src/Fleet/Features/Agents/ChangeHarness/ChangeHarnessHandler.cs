using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.ChangeHarness;

public sealed class ChangeHarnessHandler(IAgentStore store)
{
    public Result<AgentRecord> Handle(string project, AgentRecord agent, string harness)
    {
        var wanted = AgentHarness.Normalize(harness);

        if (wanted == agent.Harness)
        {
            return Result<AgentRecord>.Ok(agent);
        }

        var changed = agent with { Harness = wanted };
        store.Save(project, changed);

        return Result<AgentRecord>.Ok(changed);
    }
}
