using System.Net;
using System.Net.Sockets;

namespace Fleet.Platform.Forwards;

public sealed class LocalPorts(Func<int, bool> isFree, Func<int> ephemeral)
{
    private readonly Lock _gate = new();
    private readonly HashSet<int> _taken = [];

    public static LocalPorts Loopback() => new(BindsOnLoopback, EphemeralOnLoopback);

    public int Take(int wanted)
    {
        lock (_gate)
        {
            if (!_taken.Contains(wanted) && isFree(wanted))
            {
                _taken.Add(wanted);
                return wanted;
            }

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var other = ephemeral();
                if (other > 0 && !_taken.Contains(other) && isFree(other))
                {
                    _taken.Add(other);
                    return other;
                }
            }

            throw new IOException($"no free local port for {wanted}");
        }
    }

    public void Release(int port)
    {
        lock (_gate)
        {
            _taken.Remove(port);
        }
    }

    public bool Holds(int port)
    {
        lock (_gate)
        {
            return _taken.Contains(port);
        }
    }

    public static bool BindsOnLoopback(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static int EphemeralOnLoopback()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
