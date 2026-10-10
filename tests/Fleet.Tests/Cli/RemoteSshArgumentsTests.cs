using Fleet.Cli.Composition;

namespace Fleet.Tests.Cli;

public sealed class RemoteSshArgumentsTests
{
    [Fact]
    public void The_bridge_ssh_is_the_control_master_for_port_forwards()
    {
        var args = EmbeddedWiring.RemoteSshArguments("box", "/run/user/1000/fleet/cm-abc", "fleet");

        Assert.Equal(["-T", "-o", "ConnectTimeout=15", "-o", "ControlMaster=yes"], args.Take(5));
        Assert.Contains("ControlPath=/run/user/1000/fleet/cm-abc", args);
        Assert.Equal(["box", "fleet", "bridge"], args.TakeLast(3));
    }

    [Fact]
    public void Without_a_control_socket_the_bridge_is_a_plain_ssh()
    {
        Assert.Equal(
            ["-T", "-o", "ConnectTimeout=15", "box", "fleet", "bridge"],
            EmbeddedWiring.RemoteSshArguments("box", null, "fleet"));
    }
}
