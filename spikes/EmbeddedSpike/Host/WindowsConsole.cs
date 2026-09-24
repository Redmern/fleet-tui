using System.Runtime.InteropServices;

namespace EmbeddedSpike.Host;

// The host console on Windows. Input is read as INPUT_RECORDs with
// ReadConsoleInputW rather than as VT bytes: ENABLE_VIRTUAL_TERMINAL_INPUT stays
// off, so the prefix check sees a virtual key and modifier state no matter what
// the pane's program has asked the terminal for. That is the fix for the
// 2026-08-08 finding that win32-input-mode broke a byte-level prefix scanner.
// herdr's src/client/input/windows_vti.rs takes the same route.
internal sealed unsafe partial class WindowsConsole : IDisposable
{
    public const ushort KeyEvent = 0x0001;
    public const ushort MouseEvent = 0x0002;
    public const ushort WindowBufferSizeEvent = 0x0004;
    public const ushort FocusEvent = 0x0010;

    private const int StdInput = -10;
    private const int StdOutput = -11;

    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableEchoInput = 0x0004;
    private const uint EnableWindowInput = 0x0008;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const uint EnableVirtualTerminalInput = 0x0200;

    private const uint EnableProcessedOutput = 0x0001;
    private const uint EnableWrapAtEolOutput = 0x0002;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const uint DisableNewlineAutoReturn = 0x0008;

    private const uint Utf8 = 65001;

    private readonly nint _in;
    private readonly nint _out;
    private readonly uint _inMode;
    private readonly uint _outMode;
    private readonly uint _outputCp;
    private readonly uint _inputCp;
    private bool _restored;

    public WindowsConsole()
    {
        _in = GetStdHandle(StdInput);
        _out = GetStdHandle(StdOutput);

        if (!GetConsoleMode(_in, out _inMode) || !GetConsoleMode(_out, out _outMode))
        {
            throw new InvalidOperationException(
                "embeddedspike needs a real console: stdin/stdout are redirected");
        }

        _outputCp = GetConsoleOutputCP();
        _inputCp = GetConsoleCP();
        SetConsoleOutputCP(Utf8);
        SetConsoleCP(Utf8);

        var inMode = (_inMode | EnableWindowInput | EnableExtendedFlags)
                     & ~(EnableProcessedInput | EnableLineInput | EnableEchoInput
                         | EnableVirtualTerminalInput | EnableQuickEditMode | EnableMouseInput);
        var outMode = EnableProcessedOutput | EnableWrapAtEolOutput
                      | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn;

        SetConsoleMode(_in, inMode);
        SetConsoleMode(_out, outMode);
    }

    public string Describe() =>
        $"inMode 0x{_inMode:X}->0x{Mode(_in):X} outMode 0x{_outMode:X}->0x{Mode(_out):X} " +
        $"outputCP {_outputCp}->{GetConsoleOutputCP()}";

    public (int Cols, int Rows) Size()
    {
        if (GetConsoleScreenBufferInfo(_out, out var info))
        {
            return (info.WindowRight - info.WindowLeft + 1, info.WindowBottom - info.WindowTop + 1);
        }

        return (80, 24);
    }

    public int Read(InputRecord[] records, int timeoutMs)
    {
        if (WaitForSingleObject(_in, (uint)timeoutMs) != 0)
        {
            return 0;
        }

        fixed (InputRecord* p = records)
        {
            return ReadConsoleInputW(_in, p, (uint)records.Length, out var read) ? (int)read : -1;
        }
    }

    public void Dispose()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        SetConsoleMode(_in, _inMode);
        SetConsoleMode(_out, _outMode);
        SetConsoleOutputCP(_outputCp);
        SetConsoleCP(_inputCp);
    }

    private static uint Mode(nint handle) => GetConsoleMode(handle, out var mode) ? mode : 0;

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    public struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KeyEventRecord Key;
        [FieldOffset(4)] public short BufferSizeX;
        [FieldOffset(6)] public short BufferSizeY;
        [FieldOffset(4)] public int SetFocus;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyEventRecord
    {
        public int KeyDown;
        public ushort RepeatCount;
        public ushort VirtualKeyCode;
        public ushort VirtualScanCode;
        public char UnicodeChar;
        public uint ControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenBufferInfo
    {
        public short SizeX;
        public short SizeY;
        public short CursorX;
        public short CursorY;
        public ushort Attributes;
        public short WindowLeft;
        public short WindowTop;
        public short WindowRight;
        public short WindowBottom;
        public short MaxX;
        public short MaxY;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleOutputCP();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleCP();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleOutputCP(uint cp);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCP(uint cp);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleScreenBufferInfo(nint handle, out ScreenBufferInfo info);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadConsoleInputW(nint handle, InputRecord* records, uint length, out uint read);
}
