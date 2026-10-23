using System.Diagnostics;
using Fleet.Platform.Editors;

namespace Fleet.Tests.Platform.Editors;

public sealed class VscodeLauncherTests
{
    [Fact]
    public void It_is_available_only_when_code_is_on_path()
    {
        Assert.True(new VscodeLauncher(exe => exe == "code", _ => null).Available);
        Assert.False(new VscodeLauncher(_ => false, _ => null).Available);
    }

    [Fact]
    public void It_runs_code_with_a_new_window_on_the_folder_without_waiting()
    {
        ProcessStartInfo? started = null;
        var launcher = new VscodeLauncher(_ => true, info =>
        {
            started = info;
            return null;
        });

        var error = launcher.LaunchDetached("/work/backend/feature_login");

        Assert.Null(error);
        Assert.NotNull(started);
        Assert.False(started.UseShellExecute);
        Assert.True(started.CreateNoWindow);
        Assert.Equal("/work/backend/feature_login", started.ArgumentList[^1]);
        Assert.Contains(started.ArgumentList, a => a.Contains("code -n", StringComparison.Ordinal));
    }

    [Fact]
    public void On_windows_code_runs_through_cmd_because_it_is_a_cmd_script()
    {
        var info = VscodeLauncher.For(@"C:\work\api", windows: true);

        Assert.Equal("cmd", info.FileName);
        Assert.Equal(["/c", "code", "-n", @"C:\work\api"], info.ArgumentList);
    }

    [Fact]
    public void Elsewhere_code_runs_through_sh_with_its_output_kept_off_the_terminal()
    {
        var info = VscodeLauncher.For("/work/api", windows: false);

        Assert.Equal("sh", info.FileName);
        Assert.Contains(">/dev/null", info.ArgumentList[1], StringComparison.Ordinal);
        Assert.Equal("/work/api", info.ArgumentList[^1]);
    }

    [Fact]
    public void A_start_that_throws_comes_back_as_an_error()
    {
        var launcher = new VscodeLauncher(_ => true, _ => throw new System.ComponentModel.Win32Exception("nope"));

        Assert.Contains("nope", launcher.LaunchDetached("/work/api"));
    }
}
