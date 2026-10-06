using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.OpenAgent;

public sealed class OpenAgentHandler(
    IMuxDriver mux, IAgentStore store, bool subOrchestratorsInNvim = true, bool preferCaller = true)
{
    public async Task<Result> HandleAsync(
        string project,
        AgentRecord agent,
        string projectRoot,
        CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var window = ProjectWindows.For(mux, panes, project, projectRoot, preferCaller);
        var mine = panes.Where(p => AgentPanes.Owns(p, agent)).ToList();
        var running = mine.FirstOrDefault();

        if (Shared.Constants.AgentHarness.IsOrchestrator(agent.Harness))
        {
            var main = mine.FirstOrDefault(p => !SubBrowse.Is(p));

            if (main is not null)
            {
                if (agent.Hidden)
                {
                    await mux.MovePaneAsync(
                            main.Id,
                            new MovePaneOptions { WindowId = window, NewWindow = window is null }, ct)
                        .ConfigureAwait(false);
                }
                else if (window is not null && main.WindowId != window)
                {
                    await mux.MovePaneAsync(main.Id, new MovePaneOptions { WindowId = window }, ct)
                        .ConfigureAwait(false);
                }

                await mux.SetTitleAsync(main.Id, AgentTitle.For(agent.Repository, agent.Branch), ct).ConfigureAwait(false);

                foreach (var browser in mine.Where(SubBrowse.Is))
                {
                    await mux.KillPaneAsync(browser.Id, ct).ConfigureAwait(false);
                }

                if (agent.Hidden || !agent.Open)
                {
                    store.Save(project, agent with { Hidden = false, Open = true });
                }

                await mux.FocusPaneAsync(main.Id, ct).ConfigureAwait(false);

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

            await mux.SetTitleAsync(running.Id, AgentTitle.For(agent.Repository, agent.Branch), ct)
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
                    ? Shared.Constants.AgentHarness.OrchestratorCommand(resume: true, inNvim: subOrchestratorsInNvim)
                    : Shared.Constants.AgentHarness.Resumed(Shared.Constants.AgentHarness.CommandFor(
                        agent.Harness, withClaude: agent.RunsClaude)),
                Env = Shared.Constants.AgentHarness.SpawnEnv(agent.Harness, subOrchestratorsInNvim),
            },
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Result.Fail($"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(pane, AgentTitle.For(agent.Repository, agent.Branch), ct).ConfigureAwait(false);

        var started = orchestrator ? agent with { InNvim = subOrchestratorsInNvim } : agent;

        if (agent.Hidden || !agent.Open || started != agent)
        {
            store.Save(project, started with { Hidden = false, Open = true });
        }

        return Result.Ok();
    }
}
