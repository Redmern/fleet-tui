using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Settings;

namespace Fleet.Tests.Shared.Harness;

public class OrchestratorCommandTests
{
    private static readonly ClaudeLaunch Launch =
        ClaudeLaunch.ForAgent("techweb", string.Empty, "add-login", true, SettingsDefaults.Models);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void A_claude_orchestrator_launches_exactly_as_before(bool resume, bool inNvim)
    {
        var command = AgentHarness.OrchestratorCommand(HarnessRegistry.Claude, resume, inNvim, Launch);

        Assert.Equal(AgentHarness.OrchestratorCommand(resume, inNvim, Launch), command);
        Assert.Equal(
            AgentHarness.SpawnEnv(AgentHarness.Orchestrator, inNvim),
            AgentHarness.SpawnEnvFor(command));
    }

    [Fact]
    public void An_opencode_orchestrator_launches_its_own_binary_without_claude_arguments()
    {
        Assert.Equal(
            ["opencode"],
            AgentHarness.OrchestratorCommand(HarnessRegistry.Opencode, resume: false, inNvim: false, Launch));
    }

    [Fact]
    public void A_reopened_orchestrator_resumes_with_its_profile()
    {
        Assert.Equal(
            ["opencode", "--continue"],
            AgentHarness.OrchestratorCommand(HarnessRegistry.Opencode, resume: true, inNvim: false, Launch));
        Assert.Equal(
            ["codex", "resume", "--last"],
            AgentHarness.OrchestratorCommand(HarnessRegistry.Codex, resume: true, inNvim: false, Launch));
    }

    [Fact]
    public void In_nvim_another_harness_runs_in_a_terminal_that_takes_typed_instructions()
    {
        var fresh = AgentHarness.OrchestratorCommand(HarnessRegistry.Codex, resume: false, inNvim: true, Launch);
        var resumed = AgentHarness.OrchestratorCommand(HarnessRegistry.Codex, resume: true, inNvim: true, Launch);

        Assert.Equal([AgentHarness.Nvim, "-c"], fresh.Take(2));
        Assert.Contains("local fleet_cmd={'codex'} ", fresh[2], StringComparison.Ordinal);
        Assert.Contains("FleetTell", fresh[2], StringComparison.Ordinal);
        Assert.DoesNotContain("ClaudeCode", fresh[2], StringComparison.Ordinal);
        Assert.Contains("local fleet_cmd={'codex','resume','--last'} ", resumed[2], StringComparison.Ordinal);
        Assert.Empty(AgentHarness.SpawnEnvFor(fresh));
    }
}
