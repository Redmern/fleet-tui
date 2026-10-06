using Fleet.Features.Agents;
using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.OpenAgent;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Results;

namespace Fleet.Cli.Composition;

public static class HeadPanes
{
    public static Func<Project, AgentRecord, bool, CancellationToken, Task<Result<AgentRecord>>> SetVisible(
        IMuxDriver mux, IAgentStore store, Func<string, bool> subOrchestratorsInNvim, Action<string> trust) =>
        async (project, agent, visible, ct) =>
        {
            var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
            var running = panes.Any(p => AgentPanes.Owns(p, agent) && !SubBrowse.Is(p));

            if (visible && !running)
            {
                var active = panes.FirstOrDefault(p => p.IsActive);

                trust(agent.Worktree);

                var opened = await new OpenAgentHandler(
                        mux, store, subOrchestratorsInNvim(project.Name), preferCaller: false, Adapters.Models(project.Name))
                    .HandleAsync(project.Name, agent, project.Root, ct)
                    .ConfigureAwait(false);

                if (active is not null)
                {
                    await mux.FocusPaneAsync(active.Id, ct).ConfigureAwait(false);
                }

                return opened.Succeeded
                    ? Result<AgentRecord>.Ok(agent with { Hidden = false, Open = true })
                    : Result<AgentRecord>.Fail(opened.Error!);
            }

            if (agent.Hidden != visible)
            {
                return Result<AgentRecord>.Ok(agent);
            }

            var window = ProjectWindows.For(mux, panes, project.Name, project.Root, preferCaller: false);

            return await new HideAgentHandler(mux, store)
                .HandleAsync(project.Name, agent, window, panes, ct)
                .ConfigureAwait(false);
        };
}
