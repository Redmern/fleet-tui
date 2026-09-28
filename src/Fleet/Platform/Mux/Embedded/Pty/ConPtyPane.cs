using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Fleet.Platform.Mux.Embedded.Pty;

[SupportedOSPlatform("windows")]
public sealed unsafe partial class ConPtyPane(ConPtyApi api) : IPanePty
{
    private const int ExtendedStartupInfoPresent = 0x00080000;
    private const int CreateUnicodeEnvironment = 0x00000400;
    private const int StartfUseStdHandles = 0x00000100;
    private const nuint ProcThreadAttributePseudoConsole = 0x00020016;

    private nint _console;
    private nint _process;
    private nint _attributes;
    private FileStream? _input;
    private FileStream? _output;
    private int _exited;

    public event Action<byte[], int>? Output;

    public event Action<int>? Exited;

    public int Pid { get; private set; }

    public void Start(
        string program,
        IReadOnlyList<string> args,
        int cols,
        int rows,
        string cwd,
        IReadOnlyDictionary<string, string> env)
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, 0, 0)
            || !CreatePipe(out var outputRead, out var outputWrite, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreatePipe failed");
        }

        var hr = api.Create(Size(cols, rows), inputRead.DangerousGetHandle(), outputWrite.DangerousGetHandle(), 0, out _console);
        inputRead.Dispose();
        outputWrite.Dispose();

        if (hr != 0)
        {
            throw new Win32Exception(hr, $"CreatePseudoConsole ({api.Name}) failed with 0x{hr:X8}");
        }

        _attributes = AttributeList(_console);
        _input = new FileStream(inputWrite, FileAccess.Write, 1, false);
        _output = new FileStream(outputRead, FileAccess.Read, 1, false);

        var startup = new StartupInfoEx { Flags = StartfUseStdHandles, AttributeList = _attributes };
        startup.Size = Marshal.SizeOf<StartupInfoEx>();

        var commandLine = WindowsCommandLine.For(program, args);
        var block = EnvironmentBlock(env);
        var directory = Directory.Exists(cwd) ? cwd : Environment.CurrentDirectory;

        fixed (char* line = commandLine + "\0")
        fixed (char* environment = block)
        {
            if (!CreateProcessW(
                    null, line, 0, 0, false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    environment, directory, ref startup, out var info))
            {
                var error = Marshal.GetLastPInvokeError();
                Dispose();
                throw new Win32Exception(error, $"CreateProcess failed for {commandLine}");
            }

            _process = info.Process;
            Pid = info.ProcessId;
            CloseHandle(info.Thread);
        }

        new Thread(ReadLoop) { IsBackground = true, Name = $"conpty-read-{Pid}" }.Start();
        new Thread(WaitLoop) { IsBackground = true, Name = $"conpty-wait-{Pid}" }.Start();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_input is { } input)
        {
            input.Write(data);
            input.Flush();
        }
    }

    public void Resize(int cols, int rows)
    {
        if (_console != 0)
        {
            api.Resize(_console, Size(cols, rows));
        }
    }

    public void Dispose()
    {
        if (_process != 0 && _exited == 0)
        {
            TerminateProcess(_process, 1);
        }

        if (_console != 0)
        {
            api.Close(_console);
            _console = 0;
        }

        _input?.Dispose();
        _output?.Dispose();

        if (_attributes != 0)
        {
            DeleteProcThreadAttributeList(_attributes);
            Marshal.FreeHGlobal(_attributes);
            _attributes = 0;
        }

        if (_process != 0)
        {
            CloseHandle(_process);
            _process = 0;
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            int n;
            while (_output is { } output && (n = output.Read(buffer, 0, buffer.Length)) > 0)
            {
                Output?.Invoke(buffer, n);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    private void WaitLoop()
    {
        var process = _process;
        if (process == 0)
        {
            return;
        }

        WaitForSingleObject(process, uint.MaxValue);
        var code = GetExitCodeProcess(process, out var exit) ? exit : -1;

        if (Interlocked.Exchange(ref _exited, 1) == 0)
        {
            Exited?.Invoke(code);
        }
    }

    private static uint Size(int cols, int rows) =>
        (uint)(ushort)Math.Max(cols, 1) | ((uint)(ushort)Math.Max(rows, 1) << 16);

    private static nint AttributeList(nint console)
    {
        nuint bytes = 0;
        InitializeProcThreadAttributeList(0, 1, 0, ref bytes);
        var list = Marshal.AllocHGlobal((int)bytes);

        if (!InitializeProcThreadAttributeList(list, 1, 0, ref bytes)
            || !UpdateProcThreadAttribute(list, 0, ProcThreadAttributePseudoConsole, console, (nuint)nint.Size, 0, 0))
        {
            var error = Marshal.GetLastPInvokeError();
            Marshal.FreeHGlobal(list);
            throw new Win32Exception(error, "building the pseudoconsole attribute list failed");
        }

        return list;
    }

    private static string EnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            merged[(string)e.Key] = (string?)e.Value ?? string.Empty;
        }

        foreach (var (key, value) in overrides)
        {
            if (value.Length == 0)
            {
                merged.Remove(key);
            }
            else
            {
                merged[key] = value;
            }
        }

        var block = new StringBuilder();
        foreach (var (key, value) in merged)
        {
            block.Append(key).Append('=').Append(value).Append('\0');
        }

        return block.Append('\0').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, nint attributes, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nuint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(
        nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint list);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string? application,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        int flags,
        char* environment,
        string? directory,
        ref StartupInfoEx startup,
        out ProcessInformation info);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint code);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(nint process, out int code);
}

[SupportedOSPlatform("windows")]
public sealed unsafe class ConPtyApi
{
    public const string Variable = "FLEET_CONPTY";

    private readonly delegate* unmanaged[Stdcall]<uint, nint, nint, uint, nint*, int> _create;
    private readonly delegate* unmanaged[Stdcall]<nint, uint, int> _resize;
    private readonly delegate* unmanaged[Stdcall]<nint, void> _close;

    private ConPtyApi(string name, nint library)
    {
        Name = name;
        _create = (delegate* unmanaged[Stdcall]<uint, nint, nint, uint, nint*, int>)NativeLibrary.GetExport(library, "CreatePseudoConsole");
        _resize = (delegate* unmanaged[Stdcall]<nint, uint, int>)NativeLibrary.GetExport(library, "ResizePseudoConsole");
        _close = (delegate* unmanaged[Stdcall]<nint, void>)NativeLibrary.GetExport(library, "ClosePseudoConsole");
    }

    public string Name { get; }

    public static ConPtyApi Current => Chosen.Value;

    private static readonly Lazy<ConPtyApi> Chosen = new(() => Choose(
        Environment.GetEnvironmentVariable(Variable),
        Path.Combine(AppContext.BaseDirectory, "conpty.dll")));

    public static ConPtyApi Inbox() => new("inbox", NativeLibrary.Load("kernel32.dll"));

    public static ConPtyApi AppLocal(string conptyDll) => new(conptyDll, NativeLibrary.Load(conptyDll));

    public static ConPtyApi Choose(string? configured, string besideExecutable)
    {
        if (string.Equals(configured, "inbox", StringComparison.OrdinalIgnoreCase))
        {
            return Inbox();
        }

        foreach (var candidate in new[] { configured, besideExecutable })
        {
            if (!string.IsNullOrWhiteSpace(candidate)
                && File.Exists(candidate)
                && File.Exists(Path.Combine(Path.GetDirectoryName(candidate)!, "OpenConsole.exe")))
            {
                return AppLocal(candidate);
            }
        }

        return Inbox();
    }

    public int Create(uint size, nint input, nint output, uint flags, out nint console)
    {
        nint handle;
        var hr = _create(size, input, output, flags, &handle);
        console = handle;
        return hr;
    }

    public void Resize(nint console, uint size) => _resize(console, size);

    public void Close(nint console) => _close(console);
}
