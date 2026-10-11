using System.Diagnostics;
using Fleet.Platform.Forwards;
using Fleet.Platform.Forwards.Models;

namespace Fleet.Tests.Platform.Forwards;

public sealed class ReverseForwardTests
{
    [Fact]
    public void A_socket_forward_and_its_cancel_go_through_the_control_socket_as_a_reverse_forward()
    {
        Assert.Equal(
            ["-S", "/run/f/cm-1", "-O", "forward", "-R", "/run/user/1000/fleet-pulse/pulse-ab.sock:/run/user/1000/pulse/native", "box"],
            SshControl.ForwardSocket("/run/f/cm-1", "box", "/run/user/1000/fleet-pulse/pulse-ab.sock", "/run/user/1000/pulse/native"));
        Assert.Equal(
            ["-S", "/run/f/cm-1", "-O", "cancel", "-R", "/r/pulse-ab.sock:/l/native", "box"],
            SshControl.CancelSocket("/run/f/cm-1", "box", "/r/pulse-ab.sock", "/l/native"));
    }

    [Fact]
    public void Voice_forwarding_is_opt_in()
    {
        Assert.False(ReverseForward.OptedIn(_ => null));
        Assert.False(ReverseForward.OptedIn(_ => "0"));
        Assert.True(ReverseForward.OptedIn(name => name == ReverseForward.OptInVariable ? "1" : null));
        Assert.True(ReverseForward.OptedIn(name => name == ReverseForward.OptInVariable ? "true" : null));
    }

    [Fact]
    public void The_viewer_pulse_socket_comes_from_pulse_server_or_the_runtime_dir_and_must_exist()
    {
        string? Env(string name) => name switch
        {
            "XDG_RUNTIME_DIR" => "/run/user/1000",
            _ => null,
        };

        Assert.Equal("/run/user/1000/pulse/native", ReverseForward.LocalPulse(Env, _ => true));
        Assert.Null(ReverseForward.LocalPulse(Env, _ => false));
        Assert.Null(ReverseForward.LocalPulse(_ => null, _ => true));
        Assert.Equal(
            "/opt/pulse/native",
            ReverseForward.LocalPulse(n => n == "PULSE_SERVER" ? "unix:/opt/pulse/native" : Env(n), p => p == "/opt/pulse/native"));
        Assert.Null(ReverseForward.LocalPulse(n => n == "PULSE_SERVER" ? "tcp:box:4713" : Env(n), _ => true));
    }

    [Fact]
    public void Each_viewer_gets_its_own_stable_socket_name()
    {
        var one = ReverseForward.SocketName("laptop", "red");

        Assert.Equal(one, ReverseForward.SocketName("LAPTOP", "red"));
        Assert.NotEqual(one, ReverseForward.SocketName("desk", "red"));
        Assert.Matches("^pulse-[0-9a-f]{12}\\.sock$", one);
    }

    [Fact]
    public void The_remote_socket_dir_is_in_the_runtime_dir_or_a_private_tmp_folder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal("/run/user/1000/fleet-pulse", ReverseForward.Directory(_ => "/run/user/1000", "red"));
        Assert.Equal("/tmp/fleet-pulse-red", ReverseForward.Directory(_ => "/" + new string('x', 90), "red"));
        Assert.Equal("/tmp/fleet-pulse-red", ReverseForward.Directory(_ => null, "red"));
    }

    [Fact]
    public async Task The_remote_prepare_script_picks_the_same_dir_fleetd_looks_in_and_clears_an_old_socket()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runtime = Path.Combine(Path.GetTempPath(), $"fleet-rf-{Guid.NewGuid():N}"[..20]);
        Directory.CreateDirectory(runtime);
        try
        {
            var dir = ReverseForward.Directory(n => n == "XDG_RUNTIME_DIR" ? runtime : null, Environment.UserName);
            Directory.CreateDirectory(dir);
            var old = Path.Combine(dir, "pulse-0123456789ab.sock");
            await File.WriteAllTextAsync(old, string.Empty);

            var (exit, output) = await ShAsync(ReverseForward.Prepare("pulse-0123456789ab.sock"), runtime);

            Assert.Equal(0, exit);
            Assert.Equal(dir, output.Trim());
            Assert.False(File.Exists(old));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
        }
        finally
        {
            Directory.Delete(runtime, recursive: true);
        }
    }

    [Fact]
    public void The_prepare_command_runs_under_sh_whatever_the_login_shell()
    {
        var command = ReverseForward.Prepare("pulse-0123456789ab.sock");

        Assert.StartsWith("sh -c '", command);
        Assert.EndsWith("'", command);
        Assert.DoesNotContain("'", command[7..^1]);
    }

    [Fact]
    public void A_pane_gets_pulse_server_for_the_newest_answering_socket_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fleet-rf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var older = Path.Combine(dir, "pulse-aaaaaaaaaaaa.sock");
            var newer = Path.Combine(dir, "pulse-bbbbbbbbbbbb.sock");
            var dead = Path.Combine(dir, "pulse-cccccccccccc.sock");
            File.WriteAllText(older, string.Empty);
            File.WriteAllText(newer, string.Empty);
            File.WriteAllText(dead, string.Empty);
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-10));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(-5));
            File.SetLastWriteTimeUtc(dead, DateTime.UtcNow);

            var env = new Dictionary<string, string>();
            ReverseForward.Apply(env, dir, socket => socket != dead);

            Assert.Equal("unix:" + newer, env[ReverseForward.PulseVariable]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_pane_keeps_its_audio_alone_when_no_viewer_socket_answers()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fleet-rf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "pulse-aaaaaaaaaaaa.sock"), string.Empty);
            var env = new Dictionary<string, string>();

            ReverseForward.Apply(env, dir, _ => false);
            ReverseForward.Apply(env, Path.Combine(dir, "missing"), _ => true);

            Assert.False(env.ContainsKey(ReverseForward.PulseVariable));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task On_link_up_the_viewer_prepares_the_remote_dir_then_reverse_forwards_its_pulse_socket()
    {
        var ssh = new FakeSsh
        {
            Answer = args => args.Contains("-O") ? null : new SshResult(0, "/run/user/1000/fleet-pulse\n", string.Empty),
        };
        var lines = new List<string>();
        var pulse = new ReverseForwardPulse("/run/user/501/pulse/native", "pulse-0123456789ab.sock", ssh.RunAsync, lines.Add);

        await pulse.OpenAsync("box", "/run/f/cm-1", CancellationToken.None);

        Assert.Equal(2, ssh.Calls.Count);
        Assert.Equal(["box", ReverseForward.Prepare("pulse-0123456789ab.sock")], ssh.Calls[0].TakeLast(2));
        Assert.Equal(
            SshControl.ForwardSocket(
                "/run/f/cm-1", "box", "/run/user/1000/fleet-pulse/pulse-0123456789ab.sock", "/run/user/501/pulse/native"),
            ssh.Calls[1]);
        Assert.Contains(lines, l => l.Contains("pulse-0123456789ab.sock") && l.Contains("box"));
    }

    [Fact]
    public async Task A_failed_prepare_forwards_nothing_and_says_why()
    {
        var ssh = new FakeSsh { Answer = args => args.Contains("-O") ? null : new SshResult(1, string.Empty, "chmod: Operation not permitted") };
        var lines = new List<string>();
        var pulse = new ReverseForwardPulse("/l/native", "pulse-0123456789ab.sock", ssh.RunAsync, lines.Add);

        await pulse.OpenAsync("box", "/run/f/cm-1", CancellationToken.None);

        Assert.Single(ssh.Calls);
        Assert.Contains(lines, l => l.Contains("Operation not permitted"));
    }

    [Fact]
    public async Task A_refused_reverse_forward_is_logged()
    {
        var ssh = new FakeSsh
        {
            Answer = args => args.Contains("-O")
                ? new SshResult(255, string.Empty, "remote port forwarding failed for listen path")
                : new SshResult(0, "/tmp/fleet-pulse-red\n", string.Empty),
        };
        var lines = new List<string>();
        var pulse = new ReverseForwardPulse("/l/native", "pulse-0123456789ab.sock", ssh.RunAsync, lines.Add);

        await pulse.OpenAsync("box", "/run/f/cm-1", CancellationToken.None);

        Assert.Contains(lines, l => l.Contains("remote port forwarding failed"));
    }

    [Fact]
    public async Task Without_a_control_socket_there_is_no_reverse_forward()
    {
        var ssh = new FakeSsh();
        var lines = new List<string>();
        var pulse = new ReverseForwardPulse("/l/native", "pulse-0123456789ab.sock", ssh.RunAsync, lines.Add);

        await pulse.OpenAsync("box", null, CancellationToken.None);

        Assert.Empty(ssh.Calls);
        Assert.Contains(lines, l => l.Contains("control socket"));
    }

    private static async Task<(int Exit, string Output)> ShAsync(string command, string runtime)
    {
        var start = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(command);
        start.Environment["XDG_RUNTIME_DIR"] = runtime;
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}
