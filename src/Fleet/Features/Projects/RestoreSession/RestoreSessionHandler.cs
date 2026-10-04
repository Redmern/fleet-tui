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
    private const int AtOnce = 4;

    public async Task<int> HandleAsync(
        string project,
        string projectRoot,
        IReadOnlyList<AgentRecord> agents,
        CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var window = ProjectWindows.For(mux, panes, project, projectRoot, preferCaller: false);
        var wanted = agents.Where(a => Wanted(a, panes)).ToList();

        var restored = await Task.WhenAll(
                InOrderAsync(project, [.. wanted.Where(a => !a.Hidden)], window, ct),
                SideBySideAsync(project, [.. wanted.Where(a => a.Hidden)], window, ct))
            .ConfigureAwait(false);

        var all = restored.SelectMany(r => r).ToList();

        foreach (var agent in all.Where(a => AgentHarness.IsOrchestrator(a.Harness)))
        {
            store?.Save(project, agent with { InNvim = subOrchestratorsInNvim });
        }

        return all.Count;
    }

    private async Task<IReadOnlyList<AgentRecord>> InOrderAsync(
        string project, IReadOnlyList<AgentRecord> agents, string? window, CancellationToken ct)
    {
        var restored = new List<AgentRecord>();
        var titles = new List<Task>();

        foreach (var agent in agents)
        {
            var pane = await SpawnAsync(project, agent, window, ct).ConfigureAwait(false);

            if (pane.IsNone)
            {
                continue;
            }

            titles.Add(Task.Run(() => TitleAsync(pane, agent, ct), ct));
            restored.Add(agent);
        }

        await Task.WhenAll(titles).ConfigureAwait(false);

        return restored;
    }

    private async Task<IReadOnlyList<AgentRecord>> SideBySideAsync(
        string project, IReadOnlyList<AgentRecord> agents, string? window, CancellationToken ct)
    {
        if (agents.Count == 0)
        {
            return [];
        }

        var restored = new bool[agents.Count];

        restored[0] = await RestoreAsync(project, agents[0], window, ct).ConfigureAwait(false);

        await Parallel.ForEachAsync(
                Enumerable.Range(1, agents.Count - 1),
                new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = ct },
                async (i, token) => restored[i] = await RestoreAsync(project, agents[i], window, token).ConfigureAwait(false))
            .ConfigureAwait(false);

        return [.. agents.Where((_, i) => restored[i])];
    }

    private async Task<bool> RestoreAsync(string project, AgentRecord agent, string? window, CancellationToken ct)
    {
        var pane = await SpawnAsync(project, agent, window, ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return false;
        }

        await TitleAsync(pane, agent, ct).ConfigureAwait(false);

        return true;
    }

    private Task<PaneId> SpawnAsync(string project, AgentRecord agent, string? window, CancellationToken ct) =>
        mux.SpawnAsync(
            Options(project, agent, window, mux.Caps.HasFlag(MuxCaps.Workspaces), subOrchestratorsInNvim),
            ct);

    private Task TitleAsync(PaneId pane, AgentRecord agent, CancellationToken ct) =>
        mux.SetTitleAsync(pane, AgentTitle.For(agent.Repository, agent.Branch), ct);

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
