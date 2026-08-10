using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.OpenAgent;

public sealed class OpenAgentHandler(IMuxDriver mux, IAgentStore store)
{
    public async Task<Result> HandleAsync(
        string project,
        AgentRecord agent,
        string projectRoot,
        CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var window = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, projectRoot))?.WindowId;
        var running = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, agent.Worktree));

        if (running is not null)
        {
            if (agent.Hidden)
            {
                await Unhide(project, agent, running.Id, window, ct).ConfigureAwait(false);
            }

            await mux.FocusPaneAsync(running.Id, ct).ConfigureAwait(false);

            return Result.Ok();
        }

        if (!Directory.Exists(agent.Worktree))
        {
            return Result.Fail($"{agent.Worktree} is gone, so this agent cannot be restarted.");
        }

        var pane = await mux.SpawnAsync(
            new SpawnOptions
            {
                Cwd = agent.Worktree,
                SessionName = project,
                WindowId = window,
                Args = Shared.Constants.AgentHarness.CommandFor(agent.Harness),
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
            store.Save(project, agent with { Hidden = false });
        }

        return Result.Ok();
    }

    private async Task Unhide(
        string project, AgentRecord agent, PaneId pane, string? window, CancellationToken ct)
    {
        await mux.MovePaneAsync(
                pane,
                new MovePaneOptions { WindowId = window, NewWindow = window is null },
                ct)
            .ConfigureAwait(false);

        store.Save(project, agent with { Hidden = false });
    }
}
