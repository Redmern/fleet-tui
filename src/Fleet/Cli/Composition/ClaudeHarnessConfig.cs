using Fleet.Ports.Harness;
using Fleet.Shared.Results;

namespace Fleet.Cli.Composition;

public sealed class ClaudeHarnessConfig : IHarnessConfig
{
    public Result WriteForOrchestration(string folder, string project, string caller) =>
        ClaudeWiring.SyncFolder(project, folder, caller);
}
