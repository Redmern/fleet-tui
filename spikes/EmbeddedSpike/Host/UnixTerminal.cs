using System.Runtime.InteropServices;

namespace EmbeddedSpike.Host;

// The host terminal on Linux: termios raw mode on fd 0, window size from
// TIOCGWINSZ on fd 1. System.Console is deliberately not touched on this path;
// its Unix implementation reconfigures the terminal on first use.
internal sealed unsafe partial class UnixTerminal : IDisposable
{
    private const string Libc = "libc.so.6";
    private const ulong TiocGWinSz = 0x5413;
    private const int TcsaNow = 0;

    private readonly byte[] _saved = new byte[128];
    private readonly bool _active;

    public UnixTerminal()
    {
        fixed (byte* saved = _saved)
        {
            if (tcgetattr(0, saved) != 0)
            {
                throw new InvalidOperationException("embeddedspike needs a tty on stdin");
            }

            var raw = stackalloc byte[128];
            new Span<byte>(saved, 128).CopyTo(new Span<byte>(raw, 128));
            cfmakeraw(raw);
            _active = tcsetattr(0, TcsaNow, raw) == 0;
        }
    }

    public string Describe() => $"termios raw={_active}";

    public (int Cols, int Rows) Size()
    {
        var size = stackalloc ushort[4];
        if (ioctl(1, TiocGWinSz, size) == 0 && size[1] > 0 && size[0] > 0)
        {
            return (size[1], size[0]);
        }

        return (80, 24);
    }

    public int Read(byte[] buffer)
    {
        fixed (byte* p = buffer)
        {
            return (int)read(0, p, (nuint)buffer.Length);
        }
    }

    public void Dispose()
    {
        if (_active)
        {
            fixed (byte* saved = _saved)
            {
                tcsetattr(0, TcsaNow, saved);
            }
        }
    }

    [LibraryImport(Libc)]
    private static partial int tcgetattr(int fd, byte* termios);

    [LibraryImport(Libc)]
    private static partial int tcsetattr(int fd, int action, byte* termios);

    [LibraryImport(Libc)]
    private static partial void cfmakeraw(byte* termios);

    [LibraryImport(Libc)]
    private static partial int ioctl(int fd, ulong request, void* arg);

    [LibraryImport(Libc)]
    private static partial nint read(int fd, byte* buf, nuint count);
}
