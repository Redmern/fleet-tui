using Fleet.Shared.Constants;

namespace Fleet.Tests.Shared;

public class AgentHarnessTests
{
    [Fact]
    public void An_orchestrator_in_nvim_is_hosted_in_nvim_and_needs_no_spawn_env()
    {
        Assert.Equal(AgentHarness.Nvim, AgentHarness.CommandFor(AgentHarness.Orchestrator)[0]);
        Assert.True(AgentHarness.HostedInNvim(AgentHarness.Orchestrator));
        Assert.True(AgentHarness.HostedInNvim(AgentHarness.Orchestrator, orchestratorInNvim: true));
        Assert.Empty(AgentHarness.SpawnEnv(AgentHarness.Orchestrator));
    }

    [Fact]
    public void A_bare_orchestrator_runs_claude_with_session_persistence_and_takes_typed_instructions()
    {
        Assert.Equal(
            [AgentHarness.Claude],
            AgentHarness.CommandFor(AgentHarness.Orchestrator, orchestratorInNvim: false));
        Assert.False(AgentHarness.HostedInNvim(AgentHarness.Orchestrator, orchestratorInNvim: false));
        Assert.Equal(
            AgentHarness.SessionPersistence,
            AgentHarness.SpawnEnv(AgentHarness.Orchestrator, orchestratorInNvim: false));
    }

    [Fact]
    public void A_bare_orchestrator_resumes_with_continue()
    {
        Assert.Equal([AgentHarness.Claude], AgentHarness.OrchestratorCommand(resume: false, inNvim: false));
        Assert.Equal(
            [AgentHarness.Claude, AgentHarness.ResumeArgument],
            AgentHarness.OrchestratorCommand(resume: true, inNvim: false));
        Assert.Equal(
            AgentHarness.OrchestratorCommand(resume: true, inNvim: false),
            AgentHarness.Resumed(AgentHarness.OrchestratorCommand(resume: false, inNvim: false)));
    }

    [Fact]
    public void The_setting_does_not_change_other_harnesses()
    {
        foreach (var harness in AgentHarness.All)
        {
            Assert.Equal(
                AgentHarness.CommandFor(harness),
                AgentHarness.CommandFor(harness, orchestratorInNvim: false));
            Assert.Equal(
                AgentHarness.HostedInNvim(harness),
                AgentHarness.HostedInNvim(harness, orchestratorInNvim: false));
        }
    }
}
