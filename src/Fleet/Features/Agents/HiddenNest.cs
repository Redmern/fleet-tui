using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;

namespace Fleet.Features.Agents;

public static class HiddenNest
{
    public static string? WindowOf(
        IReadOnlyList<Pane> panes, IReadOnlyList<AgentRecord> agents, string hiddenWorkspace) =>
        panes.FirstOrDefault(p =>
            string.Equals(p.SessionName, hiddenWorkspace, StringComparison.OrdinalIgnoreCase)
            && agents.Any(a => AgentPaneMatch.Owns(p, a)))?.WindowId;

    public static async Task<string?> MoveIntoAsync(
        IMuxDriver mux,
        IEnumerable<PaneId> ids,
        string? hiddenWindow,
        string hiddenWorkspace,
        CancellationToken ct = default)
    {
        foreach (var id in ids)
        {
            if (hiddenWindow is null)
            {
                await mux.MovePaneAsync(
                        id, new MovePaneOptions { Workspace = hiddenWorkspace }, ct)
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
}
