using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Shared.Harness;

public sealed class HarnessRegistryTests
{
    [Fact]
    public void Claude_is_a_profile_with_every_capability_and_its_session_env()
    {
        var claude = HarnessRegistry.Find(AgentHarness.Claude)!;

        Assert.Equal("claude", claude.Binary);
        Assert.Equal(["--continue"], claude.ResumeArguments);
        Assert.Equal(AgentHarness.SessionPersistence, claude.SpawnEnv);
        Assert.Equal("CLAUDE.md", claude.InstructionFile);
        Assert.True(claude.Has(HarnessCapabilities.Mcp));
        Assert.True(claude.Has(HarnessCapabilities.Hooks));
        Assert.True(claude.Has(HarnessCapabilities.Skills));
        Assert.True(claude.Has(HarnessCapabilities.Permissions));
        Assert.True(claude.Has(HarnessCapabilities.NativeMessaging));
    }

    [Fact]
    public void Opencode_has_mcp_and_skills_but_no_hooks_permissions_or_native_messaging()
    {
        var opencode = HarnessRegistry.Find(AgentHarness.Opencode)!;

        Assert.Equal("opencode", opencode.Binary);
        Assert.Equal(["--continue"], opencode.ResumeArguments);
        Assert.Empty(opencode.SpawnEnv);
        Assert.Equal("AGENTS.md", opencode.InstructionFile);
        Assert.Equal(Path.Combine(".agents", "skills"), opencode.SkillsDir);
        Assert.True(opencode.Has(HarnessCapabilities.Mcp));
        Assert.True(opencode.Has(HarnessCapabilities.Skills));
        Assert.False(opencode.Has(HarnessCapabilities.Hooks));
        Assert.False(opencode.Has(HarnessCapabilities.Permissions));
        Assert.False(opencode.Has(HarnessCapabilities.NativeMessaging));
    }

    [Fact]
    public void An_unregistered_name_is_refused_with_the_valid_harnesses()
    {
        var refused = HarnessRegistry.Require("codexx");

        Assert.False(refused.Succeeded);
        Assert.Contains("'codexx'", refused.Error);
        Assert.Contains("nvim, claude, opencode", refused.Error);
    }

    [Theory]
    [InlineData("nvim", "nvim")]
    [InlineData("claude", "claude")]
    [InlineData(" opencode ", "opencode")]
    public void A_registered_name_is_accepted_trimmed(string given, string wanted)
    {
        var required = HarnessRegistry.Require(given);

        Assert.True(required.Succeeded);
        Assert.Equal(wanted, required.Value);
    }

    [Fact]
    public void Loading_stays_lenient_and_keeps_opencode()
    {
        Assert.Equal(AgentHarness.Opencode, AgentHarness.Normalize("opencode"));
        Assert.Equal(AgentHarness.Nvim, AgentHarness.Normalize("something-old"));
    }

    [Fact]
    public void Opencode_launches_bare_without_nvim_or_spawn_env()
    {
        Assert.Equal(["opencode"], AgentHarness.CommandFor(AgentHarness.Opencode));
        Assert.Equal(["opencode"], AgentHarness.CommandFor(AgentHarness.Opencode, withClaude: true));
        Assert.Empty(AgentHarness.SpawnEnv(AgentHarness.Opencode));
        Assert.False(AgentHarness.HostedInNvim(AgentHarness.Opencode));
        Assert.Equal(AgentHarness.Opencode, AgentHarness.HostedFor(AgentHarness.Opencode, nvimAllowed: false));
    }

    [Fact]
    public void Opencode_resumes_with_its_resume_arguments_once()
    {
        var resumed = AgentHarness.Resumed(["opencode"]);

        Assert.Equal(["opencode", "--continue"], resumed);
        Assert.Equal(resumed, AgentHarness.Resumed(resumed));
    }

    [Fact]
    public void A_command_of_no_known_harness_is_not_resumed()
    {
        Assert.Equal(["bash"], AgentHarness.Resumed(["bash"]));
        Assert.Empty(AgentHarness.Resumed([]));
    }

    [Fact]
    public void The_kickoff_names_the_profile_instruction_file()
    {
        Assert.Equal(AgentHarness.OrchestratorKickoff, HarnessRegistry.Claude.Kickoff);
        Assert.Equal(
            "Read AGENTS.md and TASK.md in your working directory, then begin.",
            HarnessRegistry.Find(AgentHarness.Opencode)!.Kickoff);
    }

    [Fact]
    public void The_orchestration_instructions_go_to_the_profile_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "orch");

        Assert.Equal(Path.Combine(folder, "CLAUDE.md"), OrchestrationPaths.InstructionsFile(folder));
        Assert.Equal(
            Path.Combine(folder, "AGENTS.md"),
            OrchestrationPaths.InstructionsFile(folder, HarnessRegistry.Find(AgentHarness.Opencode)!));
    }
}
