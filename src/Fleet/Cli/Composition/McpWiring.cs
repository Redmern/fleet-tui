using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Mcp.ServeMcp;
using Fleet.Features.Mcp.ServeMcp.Models;
using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Platform.Mcp;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Cli.Composition;

public static class McpWiring
{
    public static async Task<int> RunAsync(Project project, string caller, CancellationToken ct)
    {
        var log = Adapters.Log();
        var agents = Adapters.Agents();
        var git = Adapters.Git();
        var settings = Adapters.Settings();
        var states = new BranchStates(git);

        var lister = new ListAgentsHandler(agents);
        var repositories = new ListRepositoriesHandler(git);
        var reporter = new ReportStatusHandler(agents);

        var caller2 = new McpCaller(project.Name, project.Root, caller);

        async Task<McpResult> Perform(McpRequest request, CancellationToken token)
        {
            var tool = HarnessToolIds.Parse(request.Tool);

            switch (tool)
            {
                case HarnessTool.ListAgents:
                    return McpResult.Ok(ToolText.Agents(lister.Handle(project.Name)));

                case HarnessTool.ListRepositories:
                    var summaries = await repositories.HandleAsync(project.Root, token)
                        .ConfigureAwait(false);
                    return McpResult.Ok(ToolText.Repositories([.. summaries.Select(s => s.Name)]));

                case HarnessTool.AgentStatus:
                    return AgentStatus(request, lister, states, project.Name);

                case HarnessTool.LogTail:
                    var lines = log.Tail(ToolArguments.Count(request, ToolArguments.Lines, 200));
                    return McpResult.Ok(lines.Count == 0 ? "The log is empty." : string.Join('\n', lines));

                case HarnessTool.Report:
                    var reported = reporter.Handle(
                        project.Name,
                        caller,
                        ToolArguments.Text(request, ToolArguments.Status),
                        ToolArguments.Text(request, ToolArguments.Summary));
                    return reported.Succeeded
                        ? McpResult.Ok(reported.Value!)
                        : McpResult.Error(reported.Error!);

                default:
                    return McpResult.Error($"{request.Tool} is not available yet.");
            }
        }

        var dispatcher = new McpDispatcher(caller2, settings, Adapters.Approvals(), log, Perform);

        var serving = new McpServing(
            McpTools.ServerName, ToolInfos(), dispatcher.HandleAsync);

        await Adapters.McpServer(log).RunAsync(serving, ct).ConfigureAwait(false);

        return 0;
    }

    private static McpResult AgentStatus(
        McpRequest request, ListAgentsHandler lister, BranchStates states, string project)
    {
        var missing = ToolArguments.Missing(request, ToolArguments.Repository, ToolArguments.Branch);

        if (missing is not null)
        {
            return McpResult.Error(missing);
        }

        var repository = ToolArguments.Text(request, ToolArguments.Repository);
        var branch = ToolArguments.Text(request, ToolArguments.Branch);

        var agent = AgentKey.Find(lister.Handle(project), repository, branch);

        return agent is null
            ? McpResult.Error(ToolText.NotFound(repository, branch))
            : McpResult.Ok(ToolText.Agent(agent, states.For(agent.Worktree, agent.BaseRef)));
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
