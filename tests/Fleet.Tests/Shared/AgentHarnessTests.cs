using Fleet.Shared.Constants;

namespace Fleet.Tests.Shared;

public class AgentHarnessTests
{
    [Fact]
    public void An_orchestrator_in_nvim_is_hosted_in_nvim_and_needs_no_spawn_env()
    {
        Assert.Equal(AgentHarness.Nvim, AgentHarness.CommandFor(AgentHarness.Orchestrator)[0]);
        Assert.True(AgentHarness.HostedInNvim(AgentHarness.Orchestrator));
        Assert.Empty(AgentHarness.SpawnEnv(AgentHarness.Orchestrator));
    }

    [Fact]
    public void A_bare_orchestrator_runs_claude_with_session_persistence()
    {
        Assert.Equal(
            [AgentHarness.Claude],
            AgentHarness.CommandFor(AgentHarness.Orchestrator, orchestratorInNvim: false));
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
                AgentHarness.SpawnEnv(harness),
                AgentHarness.SpawnEnv(harness, orchestratorInNvim: false));
        }
    }

    public static TheoryData<string> Startups => new()
    {
        AgentHarness.NvimStartup,
        AgentHarness.NvimStartupWithClaude(string.Empty),
        AgentHarness.NvimStartupClaudeOnly(string.Empty),
    };

    [Theory]
    [MemberData(nameof(Startups))]
    public void Every_nvim_startup_sets_up_the_remote_clipboard(string startup) =>
        Assert.Contains(AgentHarness.RemoteClipboard, startup, StringComparison.Ordinal);

    [Fact]
    public void The_remote_clipboard_acts_in_a_remote_pane_or_one_without_a_clipboard_tool_and_leaves_a_user_clipboard_alone()
    {
        Assert.Contains($"vim.env.{AgentHarness.RemotePaneVariable}=='1'", AgentHarness.RemoteClipboard, StringComparison.Ordinal);
        Assert.Contains("provider#clipboard#Executable", AgentHarness.RemoteClipboard, StringComparison.Ordinal);
        Assert.Contains("vim.g.clipboard~=nil", AgentHarness.RemoteClipboard, StringComparison.Ordinal);
        Assert.Contains("VimEnter", AgentHarness.RemoteClipboard, StringComparison.Ordinal);
        Assert.Contains("TextYankPost", AgentHarness.RemoteClipboard, StringComparison.Ordinal);
        Assert.DoesNotContain("vim.o.clipboard=", AgentHarness.RemoteClipboard, StringComparison.Ordinal);
    }

    [Fact]
    public void The_remote_clipboard_never_asks_the_terminal_for_its_clipboard()
    {
        Assert.Contains("vim.ui.clipboard.osc52", AgentHarness.RemoteClipboard, StringComparison.Ordinal);
        Assert.DoesNotContain(".paste(", AgentHarness.RemoteClipboard, StringComparison.Ordinal);
        Assert.DoesNotContain("'?'", AgentHarness.RemoteClipboard, StringComparison.Ordinal);
    }

    [Fact]
    public void A_remote_pane_is_marked_for_nvim()
    {
        var env = new Dictionary<string, string> { ["PATH"] = "/bin" };

        AgentHarness.MarkRemotePane(env);

        Assert.Equal("1", env[AgentHarness.RemotePaneVariable]);
        Assert.Equal("/bin", env["PATH"]);
    }
}
