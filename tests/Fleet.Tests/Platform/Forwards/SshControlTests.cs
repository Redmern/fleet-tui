using Fleet.Platform.Forwards;

namespace Fleet.Tests.Platform.Forwards;

public sealed class SshControlTests
{
    [Fact]
    public void Forward_and_cancel_bind_only_on_loopback_through_the_control_socket()
    {
        Assert.Equal(
            ["-S", "/run/f/cm-1", "-O", "forward", "-L", "127.0.0.1:15173:127.0.0.1:5173", "box"],
            SshControl.Forward("/run/f/cm-1", "box", 15173, "127.0.0.1", 5173));
        Assert.Equal(
            ["-S", "/run/f/cm-1", "-O", "cancel", "-L", "127.0.0.1:8080:[::1]:8080", "box"],
            SshControl.Cancel("/run/f/cm-1", "box", 8080, "[::1]", 8080));
        Assert.Equal(["-S", "/run/f/cm-1", "-O", "check", "box"], SshControl.Check("/run/f/cm-1", "box"));
        Assert.Equal(["-S", "/run/f/cm-1", "-O", "exit", "box"], SshControl.Exit("/run/f/cm-1", "box"));
    }

    [Fact]
    public void Commands_run_over_the_master_and_never_prompt()
    {
        var run = SshControl.Run("/run/f/cm-1", "box", "ss -ltnH");

        Assert.Contains("BatchMode=yes", run);
        Assert.Contains("ControlMaster=no", run);
        Assert.Equal(["box", "ss -ltnH"], run.TakeLast(2));
    }

    [Fact]
    public void The_master_owns_its_socket_and_dies_with_the_link()
    {
        var options = SshControl.MasterOptions("/run/f/cm-1");

        Assert.Contains("ControlMaster=yes", options);
        Assert.Contains("ControlPath=/run/f/cm-1", options);
        Assert.Contains("ControlPersist=no", options);
        Assert.Contains("ExitOnForwardFailure=yes", options);
        Assert.Contains("ServerAliveInterval=15", options);
    }

    [Fact]
    public void A_refusing_sshd_is_recognised() =>
        Assert.True(SshControl.Prohibited("channel 3: open failed: administratively prohibited: open failed"));
}
