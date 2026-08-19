using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Agents;

public static class SubBrowse
{
    public static bool Is(Pane pane) =>
        pane.Title.EndsWith(" files", StringComparison.OrdinalIgnoreCase);

    public static string Title(AgentRecord agent) => $"{agent.Branch} files";

    public static async Task SplitAsync(
        IMuxDriver mux, AgentRecord agent, PaneId claude, CancellationToken ct = default)
    {
        var browse = await mux.SplitAsync(
            new SplitOptions(claude, SplitDirection.Right)
            {
                Percent = 50,
                Cwd = agent.Worktree,
                Args = AgentHarness.BrowseCommand,
            },
            ct).ConfigureAwait(false);

        if (!browse.IsNone)
        {
            await mux.SetTitleAsync(browse, Title(agent), ct).ConfigureAwait(false);
        }
    }
}
