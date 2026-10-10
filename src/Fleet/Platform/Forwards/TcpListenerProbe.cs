using System.Net;
using System.Net.Sockets;
using Fleet.Ports.Forwards;

namespace Fleet.Platform.Forwards;

public sealed class TcpListenerProbe(TimeSpan? timeout = null) : IListenerProbe
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(1);

    public async Task<bool> ListeningAsync(int port, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_timeout);

        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }
}
