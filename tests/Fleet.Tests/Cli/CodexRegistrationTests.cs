using Fleet.Cli.Composition;
using Fleet.Platform.Codex;
using Fleet.Platform.Harness;

namespace Fleet.Tests.Cli;

public sealed class CodexRegistrationTests
{
    [Fact]
    public void Codex_agents_and_orchestrations_get_the_codex_config_writer_once()
    {
        Assert.IsType<CodexHarnessConfig>(HarnessRegistration.NonClaudeConfigs().For("codex"));
        Assert.IsType<NullHarnessConfig>(HarnessRegistration.ClaudeConfigs().For("codex"));
    }

    [Fact]
    public void The_user_config_follows_codex_home_and_falls_back_to_the_home_folder()
    {
        Assert.Equal(Path.Combine("/ch", "config.toml"), CodexHarnessConfig.UserConfigIn("/ch", "/h"));
        Assert.Equal(Path.Combine("/h", ".codex", "config.toml"), CodexHarnessConfig.UserConfigIn(" ", "/h"));
        Assert.Equal(Path.Combine("/h", ".codex", "config.toml"), CodexHarnessConfig.UserConfigIn(null, "/h"));
    }
}
