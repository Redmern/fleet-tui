using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Requests;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.OpenAgent;

public sealed class OpenAgentHandler(IMuxDriver mux, IWorkspaceRequestStore workspaces)
{
    public async Task<Result> HandleAsync(
        string project, AgentRecord agent, CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var match = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, agent.Worktree));

        if (match is not null)
        {
            if (match.SessionName != FleetWorkspaces.Default)
            {
                workspaces.Submit(match.SessionName);
            }

            await mux.FocusPaneAsync(match.Id, ct).ConfigureAwait(false);
            return Result.Ok();
        }

        if (!Directory.Exists(agent.Worktree))
        {
            return Result.Fail(
                $"{agent.Worktree} is gone, so this agent cannot be restarted.");
        }

        var pane = await mux.SpawnAsync(
            new SpawnOptions
            {
                Cwd = agent.Worktree,
                SessionName = project,
                Workspace = agent.Hidden ? FleetWorkspaces.Hidden : null,
                Args = [agent.Harness],
            },
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Result.Fail($"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(
                pane, $"{agent.Repository}/{BranchSlug.Of(agent.Branch)}", ct)
            .ConfigureAwait(false);

        if (agent.Hidden)
        {
            workspaces.Submit(FleetWorkspaces.Hidden);
        }

        return Result.Ok();
    }
}
