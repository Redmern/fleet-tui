using Fleet.Features.Agents;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects.Models;

namespace Fleet.Cli.Composition;

public static class ProjectStructureReader
{
    public static async Task<ProjectStructure> ReadAsync(
        IGitRunner git, IMuxDriver mux, IAgentStore store, Project project, CancellationToken ct)
    {
        var repositories = await new ListRepositoriesHandler(git).HandleAsync(project.Root, ct).ConfigureAwait(false);
        var agents = new ListAgentsHandler(store).Handle(project.Name);
        var paneOpen = await PaneOpenAsync(mux, ct).ConfigureAwait(false);

        return new ProjectStructure(
            [.. repositories.Select(r => r.Name)],
            SubSummary.Text(agents, paneOpen),
            SubSummary.Unowned(agents, paneOpen));
    }

    public static async Task<Func<AgentRecord, bool>> PaneOpenAsync(IMuxDriver mux, CancellationToken ct)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        return agent => panes.Any(p => AgentPanes.Owns(p, agent) && !SubBrowse.Is(p));
    }
}
