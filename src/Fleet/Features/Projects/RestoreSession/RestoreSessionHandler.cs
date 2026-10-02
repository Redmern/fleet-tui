using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Features.Projects.RestoreSession;

public sealed class RestoreSessionHandler(
    IMuxDriver mux, bool subOrchestratorsInNvim = true, IAgentStore? store = null)
{
    public async Task<int> HandleAsync(
        string project,
        string projectRoot,
        IReadOnlyList<AgentRecord> agents,
        CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var window = ProjectWindows.For(mux, panes, project, projectRoot, preferCaller: false);

        var restored = 0;

        foreach (var agent in agents)
        {
            if (!Wanted(agent, panes))
            {
                continue;
            }

            var pane = await mux.SpawnAsync(
                    Options(project, agent, window, mux.Caps.HasFlag(MuxCaps.Workspaces), subOrchestratorsInNvim),
                    ct)
                .ConfigureAwait(false);

            if (pane.IsNone)
            {
                continue;
            }

            await mux.SetTitleAsync(pane, AgentTitle.For(agent.Repository, agent.Branch), ct)
                .ConfigureAwait(false);

            if (AgentHarness.IsOrchestrator(agent.Harness))
            {
                store?.Save(project, agent with { InNvim = subOrchestratorsInNvim });
            }

            restored++;
        }

        return restored;
    }

    public static string HiddenWorkspace(string project, bool workspaces) =>
        workspaces ? FleetWorkspaces.HiddenFor(project) : FleetWorkspaces.Hidden;

    public static bool Wanted(AgentRecord agent, IReadOnlyList<Pane> panes) =>
        agent.Open
        && Directory.Exists(agent.Worktree)
        && !panes.Any(p => PathKey.Same(p.Cwd, agent.Worktree) && !AgentPaneMatch.IsEditor(p, agent));

    public static SpawnOptions Options(
        string project,
        AgentRecord agent,
        string? window,
        bool workspaces = false,
        bool subOrchestratorsInNvim = true) =>
        new()
        {
            Cwd = agent.Worktree,
            SessionName = agent.Hidden ? HiddenWorkspace(project, workspaces) : project,
            Workspace = agent.Hidden ? HiddenWorkspace(project, workspaces) : null,
            WindowId = agent.Hidden ? null : window,
            NewWindow = agent.Hidden,
            Args = AgentHarness.IsOrchestrator(agent.Harness)
                ? AgentHarness.OrchestratorCommand(resume: true, inNvim: subOrchestratorsInNvim)
                : AgentHarness.CommandFor(agent.Harness, withClaude: agent.RunsClaude),
            Env = AgentHarness.IsOrchestrator(agent.Harness)
                ? AgentHarness.SpawnEnv(agent.Harness, subOrchestratorsInNvim)
                : new Dictionary<string, string>(),
        };
}
