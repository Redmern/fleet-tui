using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Features.Projects.RestoreSession;

public sealed class RestoreSessionHandler(IMuxDriver mux)
{
    public async Task<int> HandleAsync(
        string project,
        string projectRoot,
        IReadOnlyList<AgentRecord> agents,
        CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var dash = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, projectRoot));

        var window = dash?.WindowId;
        var native = ProjectWorkspace.IsNative(dash, project);

        var restored = 0;

        foreach (var agent in agents)
        {
            if (!Wanted(agent, panes))
            {
                continue;
            }

            var pane = await mux.SpawnAsync(Options(project, agent, window, native), ct)
                .ConfigureAwait(false);

            if (pane.IsNone)
            {
                continue;
            }

            await mux.SetTitleAsync(pane, AgentTitle.For(agent.Repository, agent.Branch), ct)
                .ConfigureAwait(false);

            restored++;
        }

        return restored;
    }

    public static bool Wanted(AgentRecord agent, IReadOnlyList<Pane> panes) =>
        agent.Open
        && Directory.Exists(agent.Worktree)
        && !panes.Any(p => PathKey.Same(p.Cwd, agent.Worktree));

    public static SpawnOptions Options(
        string project, AgentRecord agent, string? window, bool native) =>
        new()
        {
            Cwd = agent.Worktree,
            SessionName = native || !agent.Hidden ? project : FleetWorkspaces.Hidden,
            Workspace = !native && agent.Hidden ? FleetWorkspaces.Hidden : null,
            WindowId = native || !agent.Hidden ? window : null,
            NewWindow = !native && agent.Hidden,
            Args = AgentHarness.CommandFor(agent.Harness, withClaude: agent.Owner.Length > 0),
        };
}
