using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Features.Projects.QuitProject;

public sealed class QuitProjectHandler(IMuxDriver mux, IAgentStore store)
{
    public async Task<Result> HandleAsync(
        string project,
        string projectRoot,
        IReadOnlyList<AgentRecord> agents,
        CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var doomed = QuitPlan.PanesToClose(panes, projectRoot, agents);

        if (doomed.Count == 0)
        {
            return Result.Fail("Nothing of this project is open.");
        }

        Remember(project, agents, panes);

        foreach (var pane in doomed)
        {
            await mux.KillPaneAsync(pane, ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }

    private void Remember(
        string project, IReadOnlyList<AgentRecord> agents, IReadOnlyList<Pane> panes)
    {
        foreach (var agent in agents)
        {
            var open = panes.Any(p => AgentPaneMatch.Owns(p, agent));

            if (open != agent.Open)
            {
                store.Save(project, agent with { Open = open });
            }
        }
    }
}
