using Fleet.Platform.Mux.WezTerm;

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
        Assert.Contains("set \"CLAUDE_CODE_FORCE_SESSION_PERSISTENCE=1\"", argv[2]);
        Assert.Contains("set \"CLAUDE_CODE_CHILD_SESSION=\"", argv[2]);
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
}
