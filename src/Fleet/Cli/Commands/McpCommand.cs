using Fleet.Cli.Composition;
using Fleet.Cli.Models;

namespace Fleet.Cli.Commands;

public static class McpCommand
{
    public static async Task<int> RunAsync(Invocation invocation)
    {
        if (invocation.Project is null)
        {
            Console.Error.WriteLine("fleet mcp: --project <name> is required");
            return 2;
        }

        var project = Adapters.Projects().Load(invocation.Project);

        if (project is null)
        {
            Console.Error.WriteLine($"fleet mcp: no saved project '{invocation.Project}'");
            return 1;
        }

        return await McpWiring
            .RunAsync(project, invocation.Caller ?? string.Empty, CancellationToken.None)
            .ConfigureAwait(false);
    }
}
