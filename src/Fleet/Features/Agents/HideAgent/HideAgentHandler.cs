using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.HideAgent;

public sealed class HideAgentHandler(IMuxDriver mux, IAgentStore store)
{
    public async Task<Result<AgentRecord>> HandleAsync(
        string project, AgentRecord agent, string? dashboardWindow, CancellationToken ct = default)
    {
        var hiding = !agent.Hidden;

        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var pane = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, agent.Worktree));

        if (pane is not null)
        {
            await mux.MovePaneAsync(
                    pane.Id,
                    hiding
                        ? new MovePaneOptions { Workspace = FleetWorkspaces.Hidden }
                        : new MovePaneOptions
                        {
                            WindowId = dashboardWindow,
                            NewWindow = dashboardWindow is null,
                        },
                    ct)
                .ConfigureAwait(false);
        }

        var changed = agent with { Hidden = hiding };
        store.Save(project, changed);

        return Result<AgentRecord>.Ok(changed);
    }
}
