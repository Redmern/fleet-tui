using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Platform.Codex;

public sealed class CodexProfileTests
{
    private static HarnessProfile Codex => HarnessRegistry.Find("codex")!;

    [Fact]
    public void Codex_is_registered_and_selectable()
    {
        Assert.Contains(HarnessRegistry.Profiles, p => p.Name == "codex");
        Assert.Contains("codex", HarnessRegistry.Selectable);
        Assert.True(HarnessRegistry.Require(" codex ").Succeeded);
        Assert.Equal("codex", AgentHarness.Normalize("codex"));
    }

    [Fact]
    public void Codex_has_mcp_and_skills_but_no_hooks_permissions_or_native_messaging()
    {
        Assert.Equal("codex", Codex.Binary);
        Assert.Equal("AGENTS.md", Codex.InstructionFile);
        Assert.Equal(Path.Combine(".agents", "skills"), Codex.SkillsDir);
        Assert.True(Codex.Has(HarnessCapabilities.Mcp));
        Assert.True(Codex.Has(HarnessCapabilities.Skills));
        Assert.False(Codex.Has(HarnessCapabilities.Hooks));
        Assert.False(Codex.Has(HarnessCapabilities.Permissions));
        Assert.False(Codex.Has(HarnessCapabilities.NativeMessaging));
    }

    [Fact]
    public void Codex_launches_bare_without_nvim_or_spawn_env()
    {
        Assert.Equal(["codex"], AgentHarness.CommandFor("codex"));
        Assert.Equal(["codex"], AgentHarness.CommandFor("codex", withClaude: true));
        Assert.Empty(AgentHarness.SpawnEnv("codex"));
        Assert.False(AgentHarness.HostedInNvim("codex"));
    }

    [Fact]
    public void Codex_resumes_its_last_session_through_the_resume_subcommand_once()
    {
        var resumed = AgentHarness.Resumed(["codex"]);

        Assert.Equal(["codex", "resume", "--last"], resumed);
        Assert.Equal(resumed, AgentHarness.Resumed(resumed));
    }

    [Fact]
    public void A_codex_orchestrator_reads_agents_md()
    {
        Assert.Equal("Read AGENTS.md and TASK.md in your working directory, then begin.", Codex.Kickoff);
        Assert.Equal(
            Path.Combine("sub", "AGENTS.md"),
            OrchestrationPaths.InstructionsFile("sub", Codex));
    }
}
