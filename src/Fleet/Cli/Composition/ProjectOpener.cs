using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Projects.LocateProject;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;

namespace Fleet.Cli.Composition;

public static class ProjectOpener
{
    public static async Task<string?> EnsureOpenAsync(IMuxDriver mux, Project chosen)
    {
        var located = await new LocateProjectHandler(mux).HandleAsync([chosen]).ConfigureAwait(false);

        if (located.TryGetValue(chosen.Name, out var where) && where.Open)
        {
            return null;
        }

        var opened = await new OpenProjectHandler(mux)
            .HandleAsync(new OpenProjectCommand(
                chosen, AgentHarness.Orchestrator, Adapters.Executable, null, Adapters.MainOrchestratorInNvim(chosen.Name), Adapters.Models(chosen.Name)))
            .ConfigureAwait(false);

        if (!opened.Succeeded)
        {
            return opened.Error;
        }

        var runnable = new ListAgentsHandler(Adapters.Agents()).Handle(chosen.Name)
            .Where(a => Adapters.OnPath(
                AgentHarness.CommandFor(a.Harness, orchestratorInNvim: Adapters.SubOrchestratorsInNvim(chosen.Name))[0]))
            .ToList();

        await new RestoreSessionHandler(mux, Adapters.SubOrchestratorsInNvim(chosen.Name), Adapters.Agents(), Adapters.Models(chosen.Name))
            .HandleAsync(chosen.Name, chosen.Root, runnable)
            .ConfigureAwait(false);

        return null;
    }

    public static async Task<string?> EnsureOpenAsync(string name)
    {
        if (Adapters.Projects().Load(name) is not { } project)
        {
            return $"{name} is not a project here";
        }

        return await EnsureOpenAsync(Adapters.Mux(Adapters.Log()).Driver, project).ConfigureAwait(false);
    }
}