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
        var mine = panes.Where(p => PathKey.Same(p.Cwd, agent.Worktree)).ToList();

        if (AgentHarness.IsOrchestrator(agent.Harness))
        {
            if (hiding && dashboardWindow is not null)
            {
                var home = panes.FirstOrDefault(p => p.WindowId == dashboardWindow);

                if (home is not null)
                {
                    await mux.FocusPaneAsync(home.Id, ct).ConfigureAwait(false);
                }
            }
            else if (!hiding && mine.Count > 0)
            {
                await mux.FocusPaneAsync(mine[0].Id, ct).ConfigureAwait(false);
            }

            var toggled = agent with { Hidden = hiding, Open = mine.Count > 0 };
            store.Save(project, toggled);

            return Result<AgentRecord>.Ok(toggled);
        }

        var options = hiding
            ? new MovePaneOptions { Workspace = FleetWorkspaces.Hidden }
            : new MovePaneOptions { WindowId = dashboardWindow, NewWindow = dashboardWindow is null };

        foreach (var pane in mine)
        {
            await mux.MovePaneAsync(pane.Id, options, ct).ConfigureAwait(false);
            await mux.SetTitleAsync(pane.Id, BranchSlug.Of(agent.Branch), ct).ConfigureAwait(false);
        }

        var changed = agent with { Hidden = hiding, Open = mine.Count > 0 };
        store.Save(project, changed);

        return Result<AgentRecord>.Ok(changed);
    }
}
