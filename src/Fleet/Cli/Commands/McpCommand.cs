using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Head.ServeHead;

namespace Fleet.Cli.Commands;

public static class McpCommand
{
    public static async Task<int> RunAsync(Invocation invocation)
    {
        if (invocation.Arguments?.Contains(HeadLaunch.HeadFlag) == true)
        {
            return await HeadWiring.RunMcpAsync(CancellationToken.None).ConfigureAwait(false);
        }

        if (invocation.Project is null)
        {
            Console.Error.WriteLine("fleet mcp: --project <name> or --head is required");
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
