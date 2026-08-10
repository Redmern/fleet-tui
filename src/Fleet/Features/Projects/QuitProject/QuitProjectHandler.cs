using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Features.Projects.QuitProject;

public sealed class QuitProjectHandler(IMuxDriver mux)
{
    public async Task<Result> HandleAsync(
        string projectRoot,
        IReadOnlyList<AgentRecord> agents,
        CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var dashboard = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, projectRoot));

        var doomed = QuitPlan.PanesToClose(panes, dashboard?.WindowId, agents);

        if (doomed.Count == 0)
        {
            return Result.Fail("Nothing of this project is open.");
        }

        foreach (var pane in doomed)
        {
            await mux.KillPaneAsync(pane, ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }
}
