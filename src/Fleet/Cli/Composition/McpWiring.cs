using Fleet.Features.Mcp.ServeMcp;
using Fleet.Features.Mcp.ServeMcp.Models;
using Fleet.Platform.Forge;
using Fleet.Platform.Mcp;
using Fleet.Platform.Storage;
using Fleet.Ports.Forge;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Mcp;
using Fleet.Shared.Orchestrations;

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
            HarnessRegistration.ClaudeConfigs(),
            HarnessRegistration.NonClaudeConfigs(),
            Adapters.SlugNamer(),
            TimeProvider.System,
            ClaudeWiring.TrustFolder,
            log,
            ForgeProbe(),
            Processes());

        var harnesses = new CallerHarnesses(project.Name, caller, Adapters.Agents());

        var dispatcher = new McpDispatcher(
            new McpCaller(project.Name, project.Root, caller),
            Adapters.Settings(),
            new FloatingApprovalChannel(Adapters.Approvals(), mux.Driver, ApproveArgs, Adapters.Notifier()),
            log,
            actions.PerformAsync,
            ApprovalTimeout(harnesses));

        var serving = new McpServing(McpTools.ServerName, ToolInfos(), dispatcher.HandleAsync)
        {
            OnReady = ReadySignal(project.Root, caller),
        };

        await Adapters.McpServer(log).RunAsync(serving, ct).ConfigureAwait(false);

        return 0;
    }

    public static IForgeProbe ForgeProbe() =>
        new CachedForgeProbe(new HttpForgeProbe(), () => FleetPaths.ForgeCacheFile);

    public static IProcessRunner Processes() => new ProcessRunner();

    public static Func<TimeSpan?> ApprovalTimeout(CallerHarnesses harnesses) =>
        () => ApprovalDeadline.For(
            HarnessRegistry.Find(harnesses.Current())?.ClientToolTimeout ?? StrictestClientLimit);

    private static TimeSpan StrictestClientLimit { get; } = HarnessRegistry.Profiles
        .Select(p => p.ClientToolTimeout)
        .OfType<TimeSpan>()
        .Min();

    private static IReadOnlyList<string> ApproveArgs(string project, string pane) =>
        [Adapters.Executable, "approve", CommandLine.ProjectFlag, project, pane];

    private static Action? ReadySignal(string projectRoot, string caller)
    {
        if (caller.Trim().Length == 0)
        {
            return null;
        }

        var marker = OrchestrationPaths.ReadyMarker(Environment.CurrentDirectory);
        var inFolder = Path.Combine(Environment.CurrentDirectory, ".fleet", AgentHarness.ClaudeReadyFile);

        return () =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                File.WriteAllText(marker, string.Empty);
                Directory.CreateDirectory(Path.GetDirectoryName(inFolder)!);
                File.WriteAllText(inFolder, string.Empty);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        };
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
