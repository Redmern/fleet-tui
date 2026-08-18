using Fleet.Features.Mcp.ServeMcp;
using Fleet.Features.Mcp.SyncClaudeConfig;
using Fleet.Platform.Claude;
using Fleet.Platform.Git;
using Fleet.Ports.Claude.Models;
using Fleet.Shared.Mcp;
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
            [server.Name],
            Adapters.Executable,
            ["hook-dispatch", "--project", project]);

        var writer = new ClaudeConfigWriter();

        writer.EnableServer(UserSettingsPath, server.Name);
        TrustFolder(directory);

        return writer.Sync(plan);
    }

    public static Result ApproveFolder(string project, string folder, string repository, string branch)
    {
        var settings = Adapters.Settings().Load(project).MergedOverDefaults();
        var permissions = ClaudePermissionPlanner.Plan(settings);

        var server = McpRegistration.For(
            Adapters.Executable, project, McpCaller.ForAgent(repository, branch));

        var result = new ClaudeConfigWriter().SyncWorktree(server, folder, permissions.Allow);

        if (result.Succeeded)
        {
            WorktreeExcludes
                .EnsureLocallyExcludedAsync(Adapters.Git(), folder, FleetExcludes)
                .GetAwaiter()
                .GetResult();
        }

        TrustFolder(folder);

        return result;
    }

    public static void TrustFolder(string folder) =>
        new ClaudeConfigWriter().TrustFolder(ClaudeJsonPath, folder);

    private static readonly string[] FleetExcludes =
        ["/.mcp.json", "/.claude/", "/.fleet/", "/.fleet-ready"];

    private static string ClaudeJsonPath =>
        Path.Combine(Adapters.HomeDirectory, ".claude.json");

    public static ClaudeState Inspect(string directory) =>
        new ClaudeConfigWriter().Inspect(directory, McpTools.ServerName);

    private static string UserSettingsPath =>
        Path.Combine(Adapters.HomeDirectory, ".claude", "settings.json");
}
