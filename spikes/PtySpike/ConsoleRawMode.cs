using System.Runtime.InteropServices;

namespace PtySpike;

public sealed class ConsoleRawMode : IDisposable
{
    private const int StdIn = -10;
    private const int StdOut = -11;

    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableEchoInput = 0x0004;
    private const uint EnableVirtualTerminalInput = 0x0200;

    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const uint DisableNewlineAutoReturn = 0x0008;

    private readonly IntPtr _in;
    private readonly IntPtr _out;
    private readonly uint _originalIn;
    private readonly uint _originalOut;
    private readonly bool _active;

    public ConsoleRawMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _in = GetStdHandle(StdIn);
        _out = GetStdHandle(StdOut);

        if (!GetConsoleMode(_in, out _originalIn) || !GetConsoleMode(_out, out _originalOut))
        {
            return;
        }

        var rawIn = _originalIn;
        rawIn &= ~(EnableProcessedInput | EnableLineInput | EnableEchoInput);
        rawIn |= EnableVirtualTerminalInput;

        var rawOut = _originalOut | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn;

        _active = SetConsoleMode(_in, rawIn) && SetConsoleMode(_out, rawOut);
    }

    public bool Active => _active;

    public void Dispose()
    {
        if (!_active || !OperatingSystem.IsWindows())
        {
            return;
        }

        SetConsoleMode(_in, _originalIn);
        SetConsoleMode(_out, _originalOut);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
