using Fleet.Features.Mcp.SyncClaudeConfig;
using Fleet.Platform.Claude;
using Fleet.Platform.Git;
using Fleet.Platform.Harness;
using Fleet.Platform.Opencode;
using Fleet.Ports.Harness;
using Fleet.Shared.Harness;

namespace Fleet.Cli.Composition;

public static class HarnessRegistration
{
    public static IHarnessConfigs ClaudeConfigs() => new HarnessConfigs(
        new Dictionary<string, IHarnessConfig>(StringComparer.Ordinal)
        {
            [HarnessRegistry.OpencodeName] = new NullHarnessConfig(),
        },
        new ClaudeHarnessConfig());

    public static IHarnessConfigs NonClaudeConfigs() => new HarnessConfigs(
        new Dictionary<string, IHarnessConfig>(StringComparer.Ordinal)
        {
            [HarnessRegistry.OpencodeName] = Opencode(),
        },
        new NullHarnessConfig());

    private static OpencodeHarnessConfig Opencode() => new(
        (project, caller) => McpRegistration.For(Adapters.Executable, project, caller),
        FleetSkills.Embedded(),
        (folder, patterns) => WorktreeExcludes
            .EnsureLocallyExcludedAsync(Adapters.Git(), folder, patterns)
            .GetAwaiter()
            .GetResult());
}
