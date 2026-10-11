using Fleet.Platform.Forwards;

namespace Fleet.Tests.Platform.Forwards;

// sh stands in for ssh: the launcher only starts, watches and kills a process.
public sealed class TunnelProcessesTests
{
    [UnixFact]
    public async Task A_process_that_exits_reports_its_code_and_stderr()
    {
        var tunnel = new TunnelProcesses("sh").Start(["-c", "echo 'bind: Address already in use' >&2; exit 3"]);

        Assert.Equal(3, await tunnel.Exited.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("bind: Address already in use", tunnel.Errors.Trim());
    }

    [UnixFact]
    public async Task Only_the_last_lines_of_stderr_are_kept()
    {
        var tunnel = new TunnelProcesses("sh").Start(
            ["-c", "i=0; while [ $i -lt 500 ]; do echo \"channel $i: open failed\" >&2; i=$((i+1)); done; echo 'Connection reset' >&2; exit 255"]);

        await tunnel.Exited.WaitAsync(TimeSpan.FromSeconds(10));
        var lines = tunnel.Errors.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(TunnelProcesses.KeptErrorLines, lines.Length);
        Assert.Equal("Connection reset", lines[^1].Trim());
    }

    [UnixFact]
    public async Task Killing_a_running_process_ends_it()
    {
        var tunnel = new TunnelProcesses("sh").Start(["-c", "sleep 30"]);
        Assert.False(tunnel.Exited.IsCompleted);

        tunnel.Kill();

        await tunnel.Exited.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [UnixFact]
    public async Task Disposing_the_launcher_kills_every_tunnel_it_started()
    {
        var launcher = new TunnelProcesses("sh");
        var one = launcher.Start(["-c", "sleep 30"]);
        var two = launcher.Start(["-c", "sleep 30"]);

        launcher.Dispose();

        await Task.WhenAll(one.Exited, two.Exited).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_missing_program_is_an_exited_tunnel_that_says_so()
    {
        var tunnel = new TunnelProcesses("fleet-no-such-ssh").Start(["-N"]);

        Assert.Equal(127, await tunnel.Exited.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("could not run fleet-no-such-ssh", tunnel.Errors);
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (OperatingSystem.IsWindows())
            {
                Skip = "sh stands in for ssh here, and Windows has no sh";
            }
        }
    }
}
