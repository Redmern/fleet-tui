using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
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
        var mine = panes.Where(p => PathKey.Same(p.Cwd, agent.Worktree)).ToList();
        var running = mine.FirstOrDefault();

        if (Shared.Constants.AgentHarness.IsOrchestrator(agent.Harness))
        {
            if (mine.Count >= 2)
            {
                if (agent.Hidden || !agent.Open)
                {
                    store.Save(project, agent with { Hidden = false, Open = true });
                }

                await mux.FocusPaneAsync(running!.Id, ct).ConfigureAwait(false);

                return Result.Ok();
            }

            foreach (var stale in mine)
            {
                await mux.KillPaneAsync(stale.Id, ct).ConfigureAwait(false);
            }
        }
        else if (running is not null)
        {
            foreach (var member in mine)
            {
                if (agent.Hidden)
                {
                    await mux.MovePaneAsync(
                            member.Id,
                            new MovePaneOptions { WindowId = window, NewWindow = window is null },
                            ct)
                        .ConfigureAwait(false);
                }
                else if (window is not null && member.WindowId != window)
                {
                    await mux.MovePaneAsync(
                            member.Id, new MovePaneOptions { WindowId = window }, ct)
                        .ConfigureAwait(false);
                }
            }

            await mux.SetTitleAsync(running.Id, BranchSlug.Of(agent.Branch), ct)
                .ConfigureAwait(false);

            if (agent.Hidden || !agent.Open)
            {
                store.Save(project, agent with { Hidden = false, Open = true });
            }

            await mux.FocusPaneAsync(running.Id, ct).ConfigureAwait(false);

            return Result.Ok();
        }

        if (!Directory.Exists(agent.Worktree))
        {
            return Result.Fail($"{agent.Worktree} is gone, so this agent cannot be restarted.");
        }

        var orchestrator = Shared.Constants.AgentHarness.IsOrchestrator(agent.Harness);

        var pane = await mux.SpawnAsync(
            new SpawnOptions
            {
                Cwd = agent.Worktree,
                SessionName = project,
                WindowId = window,
                Args = orchestrator
                    ? [Shared.Constants.AgentHarness.Claude, Shared.Constants.AgentHarness.ResumeArgument]
                    : Shared.Constants.AgentHarness.CommandFor(agent.Harness),
                Env = Shared.Constants.AgentHarness.SpawnEnv(agent.Harness),
            },
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Result.Fail($"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(pane, BranchSlug.Of(agent.Branch), ct).ConfigureAwait(false);

        if (orchestrator)
        {
            await OpenBrowseSplit(agent, pane, ct).ConfigureAwait(false);
        }

        if (agent.Hidden || !agent.Open)
        {
            store.Save(project, agent with { Hidden = false, Open = true });
        }

        return Result.Ok();
    }

    private async Task OpenBrowseSplit(AgentRecord agent, PaneId pane, CancellationToken ct)
    {
        var browse = await mux.SplitAsync(
            new SplitOptions(pane, SplitDirection.Right)
            {
                Percent = 50,
                Cwd = agent.Worktree,
                Args = Shared.Constants.AgentHarness.BrowseCommand,
            },
            ct).ConfigureAwait(false);

        if (!browse.IsNone)
        {
            await mux.SetTitleAsync(browse, $"{agent.Branch} files", ct).ConfigureAwait(false);
        }
    }
}
