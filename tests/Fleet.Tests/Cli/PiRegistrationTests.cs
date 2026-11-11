using Fleet.Cli.Composition;
using Fleet.Platform.Harness;
using Fleet.Platform.Pi;

namespace Fleet.Tests.Cli;

public sealed class PiRegistrationTests
{
    [Fact]
    public void Pi_agents_and_orchestrations_get_the_pi_config_writer_once()
    {
        Assert.IsType<PiHarnessConfig>(HarnessRegistration.NonClaudeConfigs().For("pi"));
        Assert.IsType<NullHarnessConfig>(HarnessRegistration.ClaudeConfigs().For("pi"));
    }
}
