using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Shared.Harness;

public sealed class HostedCommandTests
{
    private static readonly ClaudeLaunch Launch = new("techweb-api-feat", new RoleModel("opus", "high"));

    [Fact]
    public void Claude_hosted_in_nvim_launches_exactly_what_nvim_with_claude_did()
    {
        Assert.Equal(
            AgentHarness.CommandFor(AgentHarness.Nvim, withClaude: true, launch: Launch),
            AgentHarness.AgentCommand(AgentHarness.Claude, hostInNvim: true, Launch));
    }

    [Fact]
    public void Claude_outside_nvim_launches_exactly_what_the_claude_harness_did()
    {
        Assert.Equal(
            AgentHarness.CommandFor(AgentHarness.Claude, launch: Launch),
            AgentHarness.AgentCommand(AgentHarness.Claude, hostInNvim: false, Launch));
    }

    [Fact]
    public void A_plain_nvim_agent_launches_the_editor_whatever_the_host_flag_says()
    {
        Assert.Equal(
            AgentHarness.CommandFor(AgentHarness.Nvim),
            AgentHarness.AgentCommand(AgentHarness.Nvim, hostInNvim: true, Launch));
    }

    [Fact]
    public void Opencode_outside_nvim_runs_bare()
    {
        Assert.Equal(["opencode"], AgentHarness.AgentCommand(AgentHarness.Opencode, hostInNvim: false, Launch));
    }

    [Fact]
    public void Opencode_hosted_in_nvim_runs_its_command_in_an_nvim_terminal()
    {
        var command = AgentHarness.AgentCommand(AgentHarness.Opencode, hostInNvim: true, Launch);

        Assert.Equal(AgentHarness.Nvim, command[0]);
        Assert.Equal("-c", command[1]);
        Assert.Contains("local fleet_cmd={'opencode'}", command[2], StringComparison.Ordinal);
        Assert.Contains("term=true", command[2], StringComparison.Ordinal);
        Assert.DoesNotContain("ClaudeCode", command[2], StringComparison.Ordinal);
        Assert.Contains("FleetTell", command[2], StringComparison.Ordinal);
    }

    [Fact]
    public void An_nvim_hosted_opencode_resumes_with_its_resume_arguments_once()
    {
        var command = AgentHarness.AgentCommand(AgentHarness.Opencode, hostInNvim: true);

        var resumed = AgentHarness.Resumed(command);

        Assert.Contains("local fleet_cmd={'opencode','--continue'}", resumed[2], StringComparison.Ordinal);
        Assert.Equal(resumed, AgentHarness.Resumed(resumed));
    }

    [Fact]
    public void A_hosted_command_quotes_lua_specials()
    {
        var startup = AgentHarness.NvimStartupWithCommand(["it's", "a\\b"]);

        Assert.Contains("local fleet_cmd={'it\\'s','a\\\\b'}", startup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_nvim_hosted_harness_gets_no_spawn_env_like_nvim_hosted_claude()
    {
        Assert.Empty(AgentHarness.SpawnEnvFor(AgentHarness.AgentCommand(AgentHarness.Claude, hostInNvim: true)));
        Assert.Empty(AgentHarness.SpawnEnvFor(AgentHarness.AgentCommand(AgentHarness.Opencode, hostInNvim: true)));
        Assert.Equal(
            HarnessRegistry.Claude.SpawnEnv,
            AgentHarness.SpawnEnvFor(AgentHarness.AgentCommand(AgentHarness.Claude, hostInNvim: false)));
    }

    [Theory]
    [InlineData("claude", true, true, "claude", true)]
    [InlineData("claude", true, false, "claude", false)]
    [InlineData("opencode", true, false, "opencode", false)]
    [InlineData("nvim", false, false, "claude", false)]
    [InlineData("nvim", true, true, "nvim", false)]
    public void Hosting_follows_whether_the_project_allows_nvim(
        string harness, bool host, bool nvimAllowed, string expected, bool expectedHost)
    {
        Assert.Equal((expected, expectedHost), AgentHarness.Hosted(harness, host, nvimAllowed));
    }

    [Fact]
    public void An_nvim_hosted_harness_needs_both_binaries()
    {
        Assert.Equal(["nvim", "opencode"], AgentHarness.Binaries(AgentHarness.Opencode, hostInNvim: true));
        Assert.Equal(["opencode"], AgentHarness.Binaries(AgentHarness.Opencode, hostInNvim: false));
        Assert.Equal(["nvim"], AgentHarness.Binaries(AgentHarness.Nvim, hostInNvim: false));
    }

    [Fact]
    public void The_description_names_the_host()
    {
        Assert.Equal("opencode in nvim", AgentHarness.Describe(AgentHarness.Opencode, hostInNvim: true));
        Assert.Equal("claude", AgentHarness.Describe(AgentHarness.Claude, hostInNvim: false));
        Assert.Equal("nvim", AgentHarness.Describe(AgentHarness.Nvim, hostInNvim: true));
    }
}
