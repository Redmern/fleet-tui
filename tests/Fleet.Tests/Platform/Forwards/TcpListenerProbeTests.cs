using System.Net;
using System.Net.Sockets;
using Fleet.Platform.Forwards;

namespace Fleet.Tests.Platform.Forwards;

public sealed class TcpListenerProbeTests
{
    [Fact]
    public async Task A_port_with_a_listener_on_loopback_is_listening()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.True(await new TcpListenerProbe().ListeningAsync(port));
    }

    [Fact]
    public async Task A_closed_port_is_not_listening()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.False(await new TcpListenerProbe().ListeningAsync(port));
    }
}
