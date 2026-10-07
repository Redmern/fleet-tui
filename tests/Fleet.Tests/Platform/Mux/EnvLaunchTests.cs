using Fleet.Platform.Mux.WezTerm;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Platform.Mux;

public sealed class EnvLaunchTests
{
    private static readonly Dictionary<string, string> Env = new()
    {
        ["CLAUDE_CODE_FORCE_SESSION_PERSISTENCE"] = "1",
        ["CLAUDE_CODE_CHILD_SESSION"] = "",
    };

    [Fact]
    public void On_windows_it_sets_the_vars_then_runs_the_command_via_cmd()
    {
        var argv = EnvLaunch.Wrap(windows: true, Env, ["claude", "--continue"]);

        Assert.Equal("cmd", argv[0]);
        Assert.Equal("/c", argv[1]);
        Assert.Contains("set CLAUDE_CODE_FORCE_SESSION_PERSISTENCE=1", argv[2]);
        Assert.Contains("set CLAUDE_CODE_CHILD_SESSION=", argv[2]);
        Assert.DoesNotContain("\"", argv[2]);
        Assert.EndsWith("claude --continue", argv[2]);
    }

    [Fact]
    public void On_windows_the_command_runs_even_if_a_set_fails()
    {
        var argv = EnvLaunch.Wrap(windows: true, Env, ["claude"]);

        Assert.DoesNotContain("&&", argv[2]);
        Assert.Contains("& ", argv[2]);
    }

    [Fact]
    public void On_unix_it_exports_sets_unsets_empties_and_execs()
    {
        var argv = EnvLaunch.Wrap(windows: false, Env, ["claude", "--continue"]);

        Assert.Equal("sh", argv[0]);
        Assert.Equal("-c", argv[1]);
        Assert.Contains("export CLAUDE_CODE_FORCE_SESSION_PERSISTENCE=1", argv[2]);
        Assert.Contains("unset CLAUDE_CODE_CHILD_SESSION", argv[2]);
        Assert.EndsWith("exec claude --continue", argv[2]);
    }

    [Fact]
    public void A_command_that_needs_quoting_runs_through_fleet_with_env_on_both_platforms()
    {
        var nvim = AgentHarness.CommandFor(AgentHarness.Nvim);
        var env = new Dictionary<string, string> { [AgentHarness.NvimAppNameVariable] = AgentHarness.FleetNvimAppName };

        foreach (var windows in new[] { true, false })
        {
            var argv = EnvLaunch.Wrap(windows, env, nvim);

            Assert.Equal(AgentHarness.WithEnvVerb, argv[1]);
            Assert.Equal("NVIM_APPNAME=fleet-nvim", argv[2]);
            Assert.Equal("--", argv[3]);
            Assert.Equal(nvim, argv.Skip(4));
        }
    }
}
