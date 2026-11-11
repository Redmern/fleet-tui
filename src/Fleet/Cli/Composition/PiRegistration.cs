using Fleet.Features.Mcp.SyncClaudeConfig;
using Fleet.Platform.Claude;
using Fleet.Platform.Git;
using Fleet.Platform.Pi;

namespace Fleet.Cli.Composition;

public static class PiRegistration
{
    public static PiHarnessConfig Config() => new(
        (project, caller) => McpRegistration.For(Adapters.Executable, project, caller),
        FleetSkills.Embedded(),
        (folder, patterns) => WorktreeExcludes
            .EnsureLocallyExcludedAsync(Adapters.Git(), folder, patterns)
            .GetAwaiter()
            .GetResult(),
        PiHarnessConfig.TrustStoreIn(
            Environment.GetEnvironmentVariable("PI_CODING_AGENT_DIR"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
}
