using Fleet.Features.Mcp.ServeMcp;
using Fleet.Features.Mcp.SyncClaudeConfig;
using Fleet.Platform.Claude;
using Fleet.Ports.Claude.Models;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;

namespace Fleet.Cli.Composition;

public static class ClaudeWiring
{
    public static Result SyncRoot(string project, string root) =>
        Sync(project, root, string.Empty, Adapters.Settings().Load(project).MergedOverDefaults());

    public static Result SyncFolder(string project, string folder, string caller) =>
        Sync(project, folder, caller, Adapters.Settings().Load(project).MergedOverDefaults());

    public static Result Sync(string project, string directory, string caller, SettingsConfig settings)
    {
        var permissions = ClaudePermissionPlanner.Plan(settings);
        var server = McpRegistration.For(Adapters.Executable, project, caller);

        var plan = new ClaudePlan(
            directory,
            server,
            permissions.Allow,
            permissions.Deny,
            permissions.Ask,
            [server.Name]);

        return new ClaudeConfigWriter().Sync(plan);
    }

    public static ClaudeState Inspect(string directory) =>
        new ClaudeConfigWriter().Inspect(directory, McpTools.ServerName);
}
