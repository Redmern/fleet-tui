using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Features.Agents;

public static class HiddenNest
{
    public static string? WindowOf(
        IReadOnlyList<Pane> panes, IReadOnlyList<AgentRecord> agents) =>
        panes.FirstOrDefault(p =>
            string.Equals(
                p.SessionName, FleetWorkspaces.Hidden, StringComparison.OrdinalIgnoreCase)
            && agents.Any(a => AgentPaneMatch.Owns(p, a)))?.WindowId;

    public static async Task<string?> MoveIntoAsync(
        IMuxDriver mux,
        IEnumerable<PaneId> ids,
        string? hiddenWindow,
        CancellationToken ct = default)
    {
        foreach (var id in ids)
        {
            if (hiddenWindow is null)
            {
                await mux.MovePaneAsync(
                        id, new MovePaneOptions { Workspace = FleetWorkspaces.Hidden }, ct)
                    .ConfigureAwait(false);

                hiddenWindow = (await mux.ListPanesAsync(ct).ConfigureAwait(false))
                    .FirstOrDefault(p => p.Id == id)?.WindowId;

                continue;
            }

            await mux.MovePaneAsync(id, new MovePaneOptions { WindowId = hiddenWindow }, ct)
                .ConfigureAwait(false);
        }

        return hiddenWindow;
    }

    public static async Task MoveIntoBackgroundAsync(
        IMuxDriver mux,
        IEnumerable<PaneId> ids,
        string dashboardWindow,
        CancellationToken ct = default)
    {
        foreach (var id in ids)
        {
            await mux.MovePaneAsync(id, new MovePaneOptions { WindowId = dashboardWindow }, ct)
                .ConfigureAwait(false);
        }
    }
}
