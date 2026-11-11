using Fleet.Features.Mcp.SyncClaudeConfig;
using Fleet.Platform.Claude;
using Fleet.Platform.Codex;
using Fleet.Platform.Git;

namespace Fleet.Cli.Composition;

public static class CodexRegistration
{
    public static CodexHarnessConfig Config() => new(
        (project, caller) => McpRegistration.For(Adapters.Executable, project, caller),
        FleetSkills.Embedded(),
        (folder, patterns) => WorktreeExcludes
            .EnsureLocallyExcludedAsync(Adapters.Git(), folder, patterns)
            .GetAwaiter()
            .GetResult(),
        CodexHarnessConfig.UserConfigIn(
            Environment.GetEnvironmentVariable("CODEX_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
}
