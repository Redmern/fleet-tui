using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Harness;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.ChangeHarness;

public sealed class ChangeHarnessHandler(IAgentStore store)
{
    public Result<AgentRecord> Handle(string project, AgentRecord agent, string harness)
    {
        var required = HarnessRegistry.Require(harness);

        if (!required.Succeeded)
        {
            return Result<AgentRecord>.Fail(required.Error!);
        }

        var wanted = required.Value!;

        if (wanted == agent.Harness)
        {
            return Result<AgentRecord>.Ok(agent);
        }

        var changed = agent with { Harness = wanted };
        store.Replace(project, changed);

        return Result<AgentRecord>.Ok(changed);
    }
}
