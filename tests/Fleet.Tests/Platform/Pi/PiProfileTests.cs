using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Platform.Pi;

public sealed class PiProfileTests
{
    private static HarnessProfile Pi => HarnessRegistry.Find("pi")!;

    [Fact]
    public void Pi_is_registered_and_selectable()
    {
        Assert.Contains(HarnessRegistry.Profiles, p => p.Name == "pi");
        Assert.Contains("pi", HarnessRegistry.Selectable);
        Assert.True(HarnessRegistry.Require(" pi ").Succeeded);
        Assert.Equal("pi", AgentHarness.Normalize("pi"));
    }

    [Fact]
    public void Pi_has_mcp_and_skills_but_no_hooks_permissions_or_native_messaging()
    {
        Assert.Equal("pi", Pi.Binary);
        Assert.Equal("AGENTS.md", Pi.InstructionFile);
        Assert.Equal(Path.Combine(".agents", "skills"), Pi.SkillsDir);
        Assert.True(Pi.Has(HarnessCapabilities.Mcp));
        Assert.True(Pi.Has(HarnessCapabilities.Skills));
        Assert.False(Pi.Has(HarnessCapabilities.Hooks));
        Assert.False(Pi.Has(HarnessCapabilities.Permissions));
        Assert.False(Pi.Has(HarnessCapabilities.NativeMessaging));
    }

    [Fact]
    public void Pi_launches_bare_without_nvim_or_spawn_env()
    {
        Assert.Equal(["pi"], AgentHarness.CommandFor("pi"));
        Assert.Equal(["pi"], AgentHarness.CommandFor("pi", withClaude: true));
        Assert.Empty(AgentHarness.SpawnEnv("pi"));
        Assert.False(AgentHarness.HostedInNvim("pi"));
    }

    [Fact]
    public void Pi_continues_the_last_session_of_the_folder_once()
    {
        var resumed = AgentHarness.Resumed(["pi"]);

        Assert.Equal(["pi", "--continue"], resumed);
        Assert.Equal(resumed, AgentHarness.Resumed(resumed));
    }

    [Fact]
    public void A_pi_orchestrator_reads_agents_md()
    {
        Assert.Equal("Read AGENTS.md and TASK.md in your working directory, then begin.", Pi.Kickoff);
        Assert.Equal(
            Path.Combine("sub", "AGENTS.md"),
            OrchestrationPaths.InstructionsFile("sub", Pi));
    }

    [Fact]
    public void Pi_names_its_npm_package_as_the_install_hint() =>
        Assert.Equal("npm install -g @earendil-works/pi-coding-agent", Pi.InstallHint);
}
