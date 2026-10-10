using Fleet.Cli.Composition;

namespace Fleet.Tests.Cli;

public sealed class RemoteSshArgumentsTests
{
    [Fact]
    public void The_bridge_ssh_is_the_control_master_for_port_forwards()
    {
        var args = EmbeddedWiring.RemoteSshArguments("box", "/run/user/1000/fleet/cm-abc", "fleet");

        Assert.Equal(["-T", "-o", "ConnectTimeout=15"], args.Take(3));
        Assert.Contains("ControlMaster=yes", args);
        Assert.Contains("ControlPath=/run/user/1000/fleet/cm-abc", args);
        Assert.Equal(["box", "fleet", "bridge"], args.TakeLast(3));
    }

    [Fact]
    public void Without_a_control_socket_the_bridge_is_a_plain_ssh()
    {
        var args = EmbeddedWiring.RemoteSshArguments("box", null, "fleet");

        Assert.DoesNotContain("ControlMaster=yes", args);
        Assert.Equal(["-T", "-o", "ConnectTimeout=15"], args.Take(3));
        Assert.Equal(["box", "fleet", "bridge"], args.TakeLast(3));
    }

    [Theory]
    [InlineData("/run/user/1000/fleet/cm-abc")]
    [InlineData(null)]
    public void The_bridge_ssh_asks_for_low_delay_and_keeps_the_link_alive(string? controlPath)
    {
        var args = EmbeddedWiring.RemoteSshArguments("box", controlPath, "fleet");

        Assert.Equal(1, args.Count(a => a == "IPQoS=lowdelay"));
        Assert.Equal(1, args.Count(a => a == "ServerAliveInterval=15"));
        Assert.Equal(1, args.Count(a => a == "ServerAliveCountMax=3"));
        Assert.DoesNotContain("-C", args);
    }

    [Fact]
    public void The_attach_ssh_asks_for_low_delay_and_keeps_the_link_alive()
    {
        var args = EmbeddedWiring.AttachSshArguments("box", "fleet");

        Assert.Contains("IPQoS=lowdelay", args);
        Assert.Contains("ServerAliveInterval=15", args);
        Assert.Equal(["-T"], args.Take(1));
        Assert.Equal(["box", "fleet", "bridge"], args.TakeLast(3));
    }
}
