using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.StopAgent;

public sealed class StopAgentHandler(IMuxDriver mux)
{
    public async Task<Result> HandleAsync(AgentRecord agent, CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var running = panes.Where(p => PathKey.Same(p.Cwd, agent.Worktree)).ToList();

        if (running.Count == 0)
        {
            return Result.Fail($"{agent.Repository}/{agent.Branch} is not running.");
        }

        foreach (var pane in running)
        {
            await mux.KillPaneAsync(pane.Id, ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }
}
