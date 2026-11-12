using Fleet.Cli.Composition;
using Fleet.Platform.Codex;
using Fleet.Platform.Opencode;
using Fleet.Shared.Harness;

namespace Fleet.Tests.Cli;

public sealed class OrchestrationConfigTests
{
    [Fact]
    public void A_sub_orchestrator_folder_is_configured_by_the_harness_it_runs()
    {
        Assert.IsType<ClaudeHarnessConfig>(HarnessRegistration.OrchestrationConfig(HarnessRegistry.Claude));
        Assert.IsType<OpencodeHarnessConfig>(HarnessRegistration.OrchestrationConfig(HarnessRegistry.Opencode));
        Assert.IsType<CodexHarnessConfig>(HarnessRegistration.OrchestrationConfig(HarnessRegistry.Codex));
    }
}
