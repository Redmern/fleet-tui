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

    [Fact]
    public void Attach_over_ssh_uses_the_same_ssh_arguments_and_control_master_as_a_remote_link()
    {
        var attach = EmbeddedWiring.AttachSsh("box");
        var link = EmbeddedWiring.RemoteSsh("box", "token", new Fleet.Platform.Mux.Embedded.Daemon.Endpoint("/tmp/fleet-test.sock"));

        Assert.Equal("ssh", attach.FileName);
        Assert.Equal(link.ArgumentList, attach.ArgumentList);
        Assert.Equal(["box", "fleet", "bridge"], attach.ArgumentList.TakeLast(3));
        if (EmbeddedWiring.ControlSocket("box") is { } socket)
        {
            Assert.Contains($"ControlPath={socket}", attach.ArgumentList);
        }
    }

    [Fact]
    public void Attach_over_ssh_keeps_its_terminal_for_password_prompts()
    {
        var attach = EmbeddedWiring.AttachSsh("box");

        Assert.False(attach.RedirectStandardError);
        Assert.False(attach.Environment.ContainsKey("SSH_ASKPASS"));
    }
}
