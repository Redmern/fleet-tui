using System.Diagnostics;
using Fleet.Platform.Editors;

namespace Fleet.Tests.Platform.Editors;

public sealed class VscodeLauncherTests
{
    private const string Hostile = @"C:\w\feat&calc %PATH% x";

    private const string HostileUnix = "/w/feat&calc %PATH% $(touch pwned) x";

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
        Assert.Contains("-n", started.ArgumentList);
    }

    [Fact]
    public void On_windows_code_runs_through_cmd_with_the_flag_and_the_path_as_separate_arguments()
    {
        var info = VscodeLauncher.Build("-n", @"C:\work\my api", windows: true);

        Assert.NotNull(info);
        Assert.Equal("cmd", info.FileName);
        Assert.Equal(["/d", "/c", "code", "-n", @"C:\work\my api"], info.ArgumentList);
        Assert.False(info.UseShellExecute);
    }

    [Theory]
    [InlineData(Hostile)]
    [InlineData(@"C:\w\a&b")]
    [InlineData(@"C:\w\100%")]
    [InlineData(@"C:\w\a|b")]
    [InlineData(@"C:\w\a^b")]
    [InlineData(@"C:\w\a<b>")]
    [InlineData(@"C:\w\a!b")]
    [InlineData("C:\\w\\a\"b")]
    public void On_windows_a_path_cmd_would_reinterpret_is_refused_rather_than_run(string target) =>
        Assert.Null(VscodeLauncher.Build("-n", target, windows: true));

    [Theory]
    [InlineData(HostileUnix)]
    [InlineData("/w/my api")]
    public void Elsewhere_the_path_is_never_part_of_the_shell_script(string target)
    {
        var info = VscodeLauncher.Build("-n", target, windows: false);

        Assert.NotNull(info);
        Assert.Equal("sh", info.FileName);
        Assert.Equal("-c", info.ArgumentList[0]);
        Assert.DoesNotContain(target, info.ArgumentList[1], StringComparison.Ordinal);
        Assert.Contains(">/dev/null", info.ArgumentList[1], StringComparison.Ordinal);
        Assert.Equal(["-n", target], info.ArgumentList.Skip(3));
    }

    [Fact]
    public void Elsewhere_the_hostile_path_reaches_code_verbatim_and_runs_nothing_else()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var fakeCode = Path.Combine(dir, "code");
        var seen = Path.Combine(dir, "seen");
        File.WriteAllText(fakeCode, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{seen}'\n");
        File.SetUnixFileMode(fakeCode, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var info = VscodeLauncher.Build("-n", HostileUnix, windows: false)!;
            info.Environment["PATH"] = dir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            info.WorkingDirectory = dir;

            using (var process = Process.Start(info)!)
            {
                process.WaitForExit();
            }

            Assert.Equal(["-n", HostileUnix], File.ReadAllLines(seen));
            Assert.False(File.Exists(Path.Combine(dir, "pwned")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_refused_path_is_reported_and_nothing_is_started()
    {
        var starts = 0;
        var launcher = new VscodeLauncher(_ => true, _ =>
        {
            starts++;
            return null;
        });

        var error = launcher.TryStart("-n", Hostile, windows: true);

        Assert.NotNull(error);
        Assert.Contains("&", error);
        Assert.Equal(0, starts);
    }

    [Fact]
    public void A_start_that_throws_comes_back_as_an_error()
    {
        var launcher = new VscodeLauncher(_ => true, _ => throw new System.ComponentModel.Win32Exception("nope"));

        Assert.Contains("nope", launcher.LaunchDetached("/work/api"));
    }

    [Fact]
    public void A_started_process_is_handed_to_the_caller_before_it_is_released()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new VscodeLauncher(_ => true, _ => Process.Start("true"));
        var handed = 0;

        var error = launcher.TryStart("--wait", "/tmp/notes.md", windows: false, started: p =>
        {
            handed++;
            p.WaitForExit();
        });

        Assert.Null(error);
        Assert.Equal(1, handed);
    }
}
