using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.ChangeHarness;

public sealed class ChangeHarnessHandler(IAgentStore store)
{
    public Result<AgentRecord> Handle(string project, AgentRecord agent, string harness, bool? hostInNvim = null)
    {
        var required = HarnessRegistry.Require(harness);

        if (!required.Succeeded)
        {
            return Result<AgentRecord>.Fail(required.Error!);
        }

        var (wanted, host) = Wanted(agent, required.Value!, hostInNvim);

        if (wanted == agent.Harness && host == agent.HostInNvim)
        {
            return Result<AgentRecord>.Ok(agent);
        }

        var changed = agent with { Harness = wanted, HostInNvim = host };
        store.Replace(project, changed);

        return Result<AgentRecord>.Ok(changed);
    }

    private static (string Harness, bool HostInNvim) Wanted(AgentRecord agent, string harness, bool? hostInNvim) =>
        hostInNvim is { } host
            ? (harness, host && AgentHarness.RunsHarness(harness))
            : !AgentHarness.RunsHarness(harness) && agent.RunsHarness
                ? (agent.Harness, true)
                : (harness, false);
}
