using Fleet.Features.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

// Sub-orchestrators used to get a file-browser split; panes left by older versions still exist.
internal static class LegacyBrowser
{
    public static async Task SplitAsync(IMuxDriver mux, AgentRecord agent, PaneId claude) =>
        await mux.SplitAsync(
            new SplitOptions(claude, SplitDirection.Right)
            {
                Percent = 50,
                Cwd = agent.Worktree,
                Args = AgentHarness.BrowseCommandFor(SubBrowse.Title(agent)),
            });
}
