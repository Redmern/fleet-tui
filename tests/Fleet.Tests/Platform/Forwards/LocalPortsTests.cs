using Fleet.Platform.Forwards;

namespace Fleet.Tests.Platform.Forwards;

public sealed class LocalPortsTests
{
    [Fact]
    public void The_remote_number_is_kept_when_it_is_free()
    {
        var ports = new LocalPorts(_ => true, () => 40000);

        Assert.Equal(5173, ports.Take(5173));
    }

    [Fact]
    public void A_busy_local_port_gets_another_free_one()
    {
        var ports = new LocalPorts(p => p != 5173, () => 40001);

        Assert.Equal(40001, ports.Take(5173));
    }

    [Fact]
    public void Two_hosts_with_the_same_remote_port_do_not_collide()
    {
        var next = 40000;
        var ports = new LocalPorts(_ => true, () => ++next);

        var first = ports.Take(3000);
        var second = ports.Take(3000);

        Assert.Equal(3000, first);
        Assert.Equal(40001, second);
        ports.Release(first);
        Assert.Equal(3000, ports.Take(3000));
    }

    [Fact]
    public void Running_out_of_ports_is_an_error()
    {
        var ports = new LocalPorts(_ => false, () => 40000);

        Assert.Throws<IOException>(() => ports.Take(3000));
    }

    [Fact]
    public void The_real_loopback_check_sees_a_bound_port_as_busy()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            Assert.False(LocalPorts.BindsOnLoopback(port));
        }
        finally
        {
            listener.Stop();
        }
    }
}
