using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Projects.QuitProject;

namespace Fleet.Cli.Commands;

public static class QuitCommand
{
    public static async Task<int> RunAsync(Invocation invocation)
    {
        if (invocation.Project is null)
        {
            Console.Error.WriteLine("fleet quit: --project <name> is required");
            return 2;
        }

        var project = Adapters.Projects().Load(invocation.Project);

        if (project is null)
        {
            Console.Error.WriteLine($"fleet quit: no saved project '{invocation.Project}'");
            return 1;
        }

        var agents = new ListAgentsHandler(Adapters.Agents()).Handle(project.Name);
        var mux = Adapters.Mux(Adapters.Log());

        var result = await new QuitProjectHandler(mux.Driver)
            .HandleAsync(project.Root, agents)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"fleet quit: {result.Error}");
            return 1;
        }

        return 0;
    }
}
