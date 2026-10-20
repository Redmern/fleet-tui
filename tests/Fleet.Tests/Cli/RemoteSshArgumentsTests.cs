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
        var args = EmbeddedWiring.AttachSsh("box").ArgumentList;

        Assert.Contains("IPQoS=lowdelay", args);
        Assert.Contains("ServerAliveInterval=15", args);
        Assert.Equal(["-T"], args.Take(1));
        Assert.Equal(["box", "fleet", "bridge"], args.TakeLast(3));
    }

    [Fact]
    public void Attach_over_ssh_uses_the_same_ssh_arguments_and_control_master_as_a_remote_link()
    {
        var attach = EmbeddedWiring.AttachSsh("box");
        var link = EmbeddedWiring.RemoteSsh("box", "token", new Fleet.Platform.Mux.Embedded.Daemon.Endpoint("/tmp/fleet-test.sock"), null);

        Assert.Equal("ssh", attach.FileName);
        Assert.Equal(WithoutControlPath(link.ArgumentList), WithoutControlPath(attach.ArgumentList));
        Assert.Equal(["box", "fleet", "bridge"], attach.ArgumentList.TakeLast(3));
        if (EmbeddedWiring.ControlSocket(EmbeddedWiring.AttachSocketKey("box")) is { } socket)
        {
            Assert.Contains("ControlMaster=yes", attach.ArgumentList);
            Assert.Contains($"ControlPath={socket}", attach.ArgumentList);
        }
    }

    [Fact]
    public void A_bridge_command_replaces_ssh_for_a_remote_link_and_learns_the_host()
    {
        var link = EmbeddedWiring.RemoteSsh(
            "box", "token", new Fleet.Platform.Mux.Embedded.Daemon.Endpoint("/tmp/fleet-test.sock"), "fleet bridge");

        Assert.NotEqual("ssh", link.FileName);
        Assert.Equal("fleet bridge", link.ArgumentList.Last());
        Assert.Equal("box", link.Environment[EmbeddedWiring.BridgeHostVariable]);
        Assert.True(link.RedirectStandardInput && link.RedirectStandardOutput);
    }

    [Fact]
    public void A_bridge_command_runs_through_the_platform_shell()
    {
        Assert.Equal(["-c", "fleet bridge"], EmbeddedWiring.BridgeCommand("fleet bridge", "box", windows: false).ArgumentList);
        Assert.Equal("/bin/sh", EmbeddedWiring.BridgeCommand("fleet bridge", "box", windows: false).FileName);
        Assert.Equal("cmd.exe", EmbeddedWiring.BridgeCommand("fleet bridge", "box", windows: true).FileName);
    }

    [Fact]
    public void An_empty_bridge_command_keeps_ssh()
    {
        Assert.Equal(
            "ssh",
            EmbeddedWiring.RemoteSsh("box", "token", new Fleet.Platform.Mux.Embedded.Daemon.Endpoint("/tmp/fleet-test.sock"), string.Empty).FileName);
    }

    // An attach that starts first must not become the master the link's port forwards ride on.
    [Fact]
    public void Attach_over_ssh_masters_its_own_socket_not_the_links()
    {
        var attach = EmbeddedWiring.AttachSsh("box");

        if (EmbeddedWiring.ControlSocket("box") is { } linkSocket)
        {
            Assert.DoesNotContain($"ControlPath={linkSocket}", attach.ArgumentList);
        }
    }

    private static IEnumerable<string> WithoutControlPath(IEnumerable<string> args) =>
        args.Where(a => !a.StartsWith("ControlPath=", StringComparison.Ordinal));

    [Fact]
    public void Attach_over_ssh_keeps_its_terminal_for_password_prompts()
    {
        var attach = EmbeddedWiring.AttachSsh("box");

        Assert.False(attach.RedirectStandardError);
        Assert.False(attach.Environment.ContainsKey("SSH_ASKPASS"));
    }

    [Fact]
    public void A_host_that_ssh_would_read_as_an_option_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => EmbeddedWiring.RemoteSshArguments("-oProxyCommand=calc", null, "fleet"));
}
