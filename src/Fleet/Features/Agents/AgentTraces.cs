using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Status;

namespace Fleet.Features.Agents;

public static class AgentTraces
{
    public static async Task ForgetAsync(IAgentStateStore? hookState, string worktree, CancellationToken ct = default)
    {
        if (hookState is not null)
        {
            await hookState.ForgetAsync(worktree, ct).ConfigureAwait(false);
        }

        PaneQuiet.Forget(worktree);
    }

    public static void Opened(AgentRecord agent)
    {
        if (agent.RunningHarness != AgentHarness.Claude)
        {
            ClosingMark.Clear(agent.Worktree);
        }
    }
}
