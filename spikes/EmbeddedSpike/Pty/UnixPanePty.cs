using System.Runtime.InteropServices;
using System.Text;

namespace EmbeddedSpike.Pty;

// openpty + posix_spawn, with no fork in managed code.
//
// DESIGN.md's PTY section says posix_spawn cannot give the child a controlling
// terminal because it cannot issue TIOCSCTTY. It does not need to: on Linux a
// session leader with no controlling terminal acquires one by opening a tty
// without O_NOCTTY. glibc's posix_spawn applies POSIX_SPAWN_SETSID before the
// file actions, so "setsid, then open /dev/pts/N as fd 0" happens in the child
// in that order, inside libc, between its internal clone and exec.
//
// Linux/glibc only. The flag value and struct sizes below are glibc's; macOS
// would need POSIX_SPAWN_SETSID = 0x400 and its own sizes.
internal sealed unsafe partial class UnixPanePty : IPanePty
{
    private const string Libc = "libc.so.6";
    private const short PosixSpawnSetSigDef = 0x04;
    private const short PosixSpawnSetSigMask = 0x08;
    private const short PosixSpawnSetSid = 0x80;
    private const int ORdWr = 0x2;
    private const ulong TiocSWinSz = 0x5414;
    private const int EIntr = 4;

    // HUP INT QUIT PIPE TERM CHLD TSTP TTIN TTOU WINCH: whatever the .NET runtime
    // installed or ignored for these must not leak into the child.
    private static readonly int[] ResetSignals = [1, 2, 3, 13, 15, 17, 20, 21, 22, 28];

    private int _master = -1;
    private int _pid = -1;
    private Thread? _reader;

    public event Action<byte[], int>? Output;

    public event Action<int>? Exited;

    public void Start(string program, IReadOnlyList<string> args, int cols, int rows)
    {
        var size = new WinSize { Rows = (ushort)rows, Cols = (ushort)cols };
        if (OpenPty(out var master, out var slave, null, null, &size) != 0)
        {
            throw new InvalidOperationException($"openpty failed, errno {Marshal.GetLastPInvokeError()}");
        }

        var slavePath = TtyName(slave);
        _master = master;

        var fileActions = stackalloc byte[256];
        var attr = stackalloc byte[512];
        var sigset = stackalloc byte[128];

        Check(posix_spawn_file_actions_init(fileActions), "file_actions_init");
        Check(posix_spawnattr_init(attr), "spawnattr_init");

        try
        {
            Check(posix_spawn_file_actions_addclose(fileActions, master), "addclose master");
            Check(posix_spawn_file_actions_addclose(fileActions, slave), "addclose slave");
            Check(posix_spawn_file_actions_addopen(fileActions, 0, slavePath, ORdWr, 0), "addopen tty");
            Check(posix_spawn_file_actions_adddup2(fileActions, 0, 1), "dup2 stdout");
            Check(posix_spawn_file_actions_adddup2(fileActions, 0, 2), "dup2 stderr");

            sigemptyset(sigset);
            Check(posix_spawnattr_setsigmask(attr, sigset), "setsigmask");
            sigemptyset(sigset);
            foreach (var signal in ResetSignals)
            {
                sigaddset(sigset, signal);
            }

            Check(posix_spawnattr_setsigdefault(attr, sigset), "setsigdefault");
            Check(posix_spawnattr_setflags(attr, (short)(PosixSpawnSetSid | PosixSpawnSetSigMask | PosixSpawnSetSigDef)), "setflags");

            var argv = new List<string> { program };
            argv.AddRange(args);

            var env = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .Where(e => (string)e.Key is not ("TERM" or "COLORTERM" or "COLUMNS" or "LINES"))
                .Select(e => $"{e.Key}={e.Value}")
                .Append("TERM=xterm-256color")
                .Append("COLORTERM=truecolor")
                .ToList();

            using var argvBlock = new CStringArray(argv);
            using var envBlock = new CStringArray(env);

            int pid;
            var rc = posix_spawnp(&pid, program, fileActions, attr, argvBlock.Pointer, envBlock.Pointer);
            if (rc != 0)
            {
                throw new InvalidOperationException($"posix_spawnp({program}) failed, error {rc}");
            }

            _pid = pid;
        }
        finally
        {
            posix_spawn_file_actions_destroy(fileActions);
            posix_spawnattr_destroy(attr);
            close(slave);
        }

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "pty-reader" };
        _reader.Start();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
        {
            var offset = 0;
            while (offset < data.Length)
            {
                var n = write(_master, p + offset, (nuint)(data.Length - offset));
                if (n < 0)
                {
                    if (Marshal.GetLastPInvokeError() == EIntr)
                    {
                        continue;
                    }

                    return;
                }

                offset += (int)n;
            }
        }
    }

    public void Resize(int cols, int rows)
    {
        var size = new WinSize { Rows = (ushort)rows, Cols = (ushort)cols };
        ioctl(_master, TiocSWinSz, &size);
    }

    public void Dispose()
    {
        if (_pid > 0)
        {
            kill(_pid, 1);
        }

        if (_master >= 0)
        {
            close(_master);
            _master = -1;
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[64 * 1024];
        while (true)
        {
            nint n;
            fixed (byte* p = buffer)
            {
                n = read(_master, p, (nuint)buffer.Length);
            }

            if (n < 0 && Marshal.GetLastPInvokeError() == EIntr)
            {
                continue;
            }

            if (n <= 0)
            {
                break;
            }

            Output?.Invoke(buffer, (int)n);
        }

        var status = 0;
        waitpid(_pid, &status, 0);
        _pid = -1;
        Exited?.Invoke((status >> 8) & 0xff);
    }

    private static string TtyName(int fd)
    {
        var buffer = stackalloc byte[256];
        if (ttyname_r(fd, buffer, 256) != 0)
        {
            throw new InvalidOperationException("ttyname_r failed");
        }

        return Marshal.PtrToStringUTF8((nint)buffer)!;
    }

    private static int OpenPty(out int master, out int slave, byte* name, void* termios, WinSize* size)
    {
        try
        {
            return openpty(out master, out slave, name, termios, size);
        }
        catch (EntryPointNotFoundException)
        {
            return openpty_libutil(out master, out slave, name, termios, size);
        }
    }

    private static void Check(int rc, string what)
    {
        if (rc != 0)
        {
            throw new InvalidOperationException($"{what} failed, error {rc}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort Rows;
        public ushort Cols;
        public ushort XPixel;
        public ushort YPixel;
    }

    private sealed class CStringArray : IDisposable
    {
        private readonly nint[] _strings;
        private readonly nint _array;

        public CStringArray(IReadOnlyList<string> values)
        {
            _strings = new nint[values.Count];
            _array = Marshal.AllocHGlobal(nint.Size * (values.Count + 1));
            for (var i = 0; i < values.Count; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(values[i] + "\0");
                _strings[i] = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, _strings[i], bytes.Length);
                Marshal.WriteIntPtr(_array, i * nint.Size, _strings[i]);
            }

            Marshal.WriteIntPtr(_array, values.Count * nint.Size, 0);
        }

        public byte** Pointer => (byte**)_array;

        public void Dispose()
        {
            foreach (var s in _strings)
            {
                Marshal.FreeHGlobal(s);
            }

            Marshal.FreeHGlobal(_array);
        }
    }

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int openpty(out int master, out int slave, byte* name, void* termios, WinSize* size);

    [LibraryImport("libutil.so.1", EntryPoint = "openpty", SetLastError = true)]
    private static partial int openpty_libutil(out int master, out int slave, byte* name, void* termios, WinSize* size);

    [LibraryImport(Libc)]
    private static partial int ttyname_r(int fd, byte* buf, nuint len);

    [LibraryImport(Libc)]
    private static partial int posix_spawn_file_actions_init(void* actions);

    [LibraryImport(Libc)]
    private static partial int posix_spawn_file_actions_destroy(void* actions);

    [LibraryImport(Libc)]
    private static partial int posix_spawn_file_actions_addclose(void* actions, int fd);

    [LibraryImport(Libc, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int posix_spawn_file_actions_addopen(void* actions, int fd, string path, int oflag, uint mode);

    [LibraryImport(Libc)]
    private static partial int posix_spawn_file_actions_adddup2(void* actions, int fd, int newfd);

    [LibraryImport(Libc)]
    private static partial int posix_spawnattr_init(void* attr);

    [LibraryImport(Libc)]
    private static partial int posix_spawnattr_destroy(void* attr);

    [LibraryImport(Libc)]
    private static partial int posix_spawnattr_setflags(void* attr, short flags);

    [LibraryImport(Libc)]
    private static partial int posix_spawnattr_setsigmask(void* attr, void* sigset);

    [LibraryImport(Libc)]
    private static partial int posix_spawnattr_setsigdefault(void* attr, void* sigset);

    [LibraryImport(Libc)]
    private static partial int sigemptyset(void* set);

    [LibraryImport(Libc)]
    private static partial int sigaddset(void* set, int signal);

    [LibraryImport(Libc, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int posix_spawnp(int* pid, string file, void* actions, void* attr, byte** argv, byte** envp);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial nint read(int fd, byte* buf, nuint count);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial nint write(int fd, byte* buf, nuint count);

    [LibraryImport(Libc)]
    private static partial int close(int fd);

    [LibraryImport(Libc)]
    private static partial int ioctl(int fd, ulong request, void* arg);

    [LibraryImport(Libc)]
    private static partial int waitpid(int pid, int* status, int options);

    [LibraryImport(Libc)]
    private static partial int kill(int pid, int sig);
}
