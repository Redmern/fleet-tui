using Fleet.Features.Mcp.ServeMcp;
using Fleet.Features.Mcp.ServeMcp.Models;
using Fleet.Platform.Mcp;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Mcp;

namespace Fleet.Cli.Composition;

public static class McpWiring
{
    public static async Task<int> RunAsync(Project project, string caller, CancellationToken ct)
    {
        var log = Adapters.Log();
        var mux = Adapters.Mux(log);

        var actions = new McpActions(
            project.Name,
            project.Root,
            caller,
            Adapters.Git(),
            mux.Driver,
            Adapters.Agents(),
            Adapters.HarnessConfig(),
            log);

        var dispatcher = new McpDispatcher(
            new McpCaller(project.Name, project.Root, caller),
            Adapters.Settings(),
            Adapters.Approvals(),
            log,
            actions.PerformAsync);

        var serving = new McpServing(McpTools.ServerName, ToolInfos(), dispatcher.HandleAsync);

        await Adapters.McpServer(log).RunAsync(serving, ct).ConfigureAwait(false);

        return 0;
    }

    private static IReadOnlyList<McpToolInfo> ToolInfos() =>
    [
        .. McpTools.All.Select(spec => new McpToolInfo(
            spec.Name,
            spec.Description,
            ToolSchema.For([.. spec.Params.Select(Field)]))),
    ];

    private static SchemaField Field(ToolParam param) =>
        new(param.Name, param.Type, param.Description, param.Required);
}
