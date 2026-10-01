using System.IO.Pipes;
using System.Net.Sockets;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public sealed class Endpoint(string address)
{
    public const string Variable = "FLEET_ENDPOINT";

    public string Address { get; } = address;

    public static Endpoint Default()
    {
        var configured = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new Endpoint(configured.Trim());
        }

        if (OperatingSystem.IsWindows())
        {
            return new Endpoint($"fleet-embedded-{Environment.UserName}");
        }

        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var dir = string.IsNullOrWhiteSpace(runtime)
            ? Path.Combine(Path.GetTempPath(), $"fleet-{Environment.UserName}")
            : Path.Combine(runtime, "fleet");

        return new Endpoint(Path.Combine(dir, "embedded.sock"));
    }

    public async Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", Address, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
                return pipe;
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(Address), limit.Token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public IListener Listen() =>
        OperatingSystem.IsWindows() ? new PipeListener(Address) : new SocketListener(Address);

    public interface IListener : IDisposable
    {
        Task<Stream> AcceptAsync(CancellationToken ct);
    }

    private sealed class PipeListener(string name) : IListener
    {
        public async Task<Stream> AcceptAsync(CancellationToken ct)
        {
            var server = new NamedPipeServerStream(
                name,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                return server;
            }
            catch
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class SocketListener : IListener
    {
        private readonly Socket _socket;
        private readonly string _path;

        public SocketListener(string path)
        {
            _path = path;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _socket.Bind(new UnixDomainSocketEndPoint(path));
            _socket.Listen(16);
        }

        public async Task<Stream> AcceptAsync(CancellationToken ct)
        {
            var client = await _socket.AcceptAsync(ct).ConfigureAwait(false);
            return new NetworkStream(client, ownsSocket: true);
        }

        public void Dispose()
        {
            _socket.Dispose();
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
        }
    }
}
