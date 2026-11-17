using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Shared;
using Fleet.Shared.Results;
using Fleet.Shared.Status;

namespace Fleet.Features.Agents.StopAgent;

public sealed class StopAgentHandler(
    IMuxDriver mux, IAgentStore store, IAgentStateStore? hookState = null, IFleetLog? log = null)
{
    public async Task<Result> HandleAsync(
        string project, AgentRecord agent, CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var running = panes
            .Where(p => PathKey.Same(p.Cwd, agent.Worktree) && !AgentPaneMatch.IsEditor(p, agent))
            .ToList();

        if (running.Count == 0)
        {
            return Result.Fail($"{agent.Repository}/{agent.Branch} is not running.");
        }

        ClosingMark.Write(agent.Worktree);

        if (agent.Open && !store.Update(project, agent.Worktree, current => current with { Open = false }))
        {
            log?.Write(LogTag.For(project, $"could not close the record of {agent.Repository}/{agent.Branch} before stopping it"));
        }

        foreach (var pane in running)
        {
            await mux.KillPaneAsync(pane.Id, ct).ConfigureAwait(false);
        }

        await AgentTraces.ForgetAsync(hookState, agent.Worktree, ct).ConfigureAwait(false);

        return Result.Ok();
    }
}
