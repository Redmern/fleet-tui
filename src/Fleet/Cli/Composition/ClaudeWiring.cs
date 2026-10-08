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

    public static Result SyncFolder(string project, string folder, string caller)
    {
        FleetSkills.WriteTo(folder);

        return Sync(project, folder, caller, Adapters.Settings().Load(project).MergedOverDefaults());
    }

    private static readonly Lock ConfigGate = new();

    public static Result Sync(string project, string directory, string caller, SettingsConfig settings)
    {
        lock (ConfigGate)
        {
            return SyncLocked(project, directory, caller, settings);
        }
    }

    private static Result SyncLocked(string project, string directory, string caller, SettingsConfig settings)
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
            ["hook-dispatch", "--project", project],
            StatusHook(settings));

        var writer = new ClaudeConfigWriter();

        writer.EnableServer(UserSettingsPath, server.Name);
        TrustFolder(directory);

        return writer.Sync(plan);
    }

    public static Result ApproveFolder(string project, string folder, string repository, string branch)
    {
        var result = ResyncWorktree(project, folder, repository, branch);

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

    public static Result ResyncWorktree(string project, string folder, string repository, string branch)
    {
        var settings = Adapters.Settings().Load(project).MergedOverDefaults();
        var permissions = ClaudePermissionPlanner.Plan(settings);

        var server = McpRegistration.For(
            Adapters.Executable, project, McpCaller.ForAgent(repository, branch));

        FleetSkills.WriteTo(folder);

        return new ClaudeConfigWriter()
            .SyncWorktree(server, folder, permissions.Allow, permissions.Deny, permissions.Ask, StatusHook(settings));
    }

    private static string StatusHook(SettingsConfig settings) =>
        settings.StatusHooks ? Adapters.Executable : string.Empty;

    public static void TrustFolder(string folder)
    {
        lock (ConfigGate)
        {
            new ClaudeConfigWriter().TrustFolder(ClaudeJsonPathFor(folder), folder, McpTools.ServerName);
        }
    }

    public static readonly IReadOnlyList<string> FleetExcludes =
        ["/.mcp.json", "/.claude/", "/.fleet/", "/.fleet-ready"];

    private static string ClaudeJsonPathFor(string folder) =>
        Path.Combine(ClaudeConfigHome.ForFolder(folder, Adapters.HomeDirectory), ".claude.json");

    public static ClaudeState Inspect(string directory) =>
        new ClaudeConfigWriter().Inspect(directory, McpTools.ServerName);

    private static string UserSettingsPath =>
        Path.Combine(Adapters.HomeDirectory, ".claude", "settings.json");
}
