using System.Runtime.InteropServices;
using EmbeddedSpike.Host;

namespace EmbeddedSpike;

// Test helper: attaches to another process's console and writes INPUT_RECORDs
// into its input buffer, so a running spike reads them with ReadConsoleInputW
// exactly as it would read a keyboard. window:WxH resizes the conhost window in
// pixels, the way dragging its frame does. (SetConsoleScreenBufferSize is no use
// here: it fails with ERROR_INVALID_PARAMETER while the alternate screen is up.)
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static unsafe partial class Injector
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 0x3;
    private const uint OpenExisting = 3;
    private const uint LeftCtrl = 0x0008;
    private const uint LeftAlt = 0x0002;
    private const uint Shift = 0x0010;

    public static int Run(string[] args)
    {
        if (args.Length < 2 || !uint.TryParse(args[0], out var pid))
        {
            Console.Error.WriteLine("usage: embeddedspike --inject PID TOKEN...");
            return 2;
        }

        FreeConsole();
        if (!AttachConsole(pid))
        {
            return 3;
        }

        var input = CreateFileW("CONIN$", GenericRead | GenericWrite, ShareReadWrite, 0, OpenExisting, 0, 0);
        
        foreach (var token in args[1..])
        {
            if (token.StartsWith("sleep:", StringComparison.Ordinal))
            {
                Thread.Sleep(int.Parse(token[6..], System.Globalization.CultureInfo.InvariantCulture));
                continue;
            }

            if (token.StartsWith("window:", StringComparison.Ordinal))
            {
                var px = token[7..].Split('x');
                var hwnd = GetConsoleWindow();
                var moved = MoveWindow(hwnd, 40, 40,
                    int.Parse(px[0], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(px[1], System.Globalization.CultureInfo.InvariantCulture), true);
                Trace($"window {token} hwnd=0x{hwnd:X} moved={moved}");
                continue;
            }

            foreach (var (vk, ch, state) in Keys(token))
            {
                Tap(input, vk, ch, state);
            }
        }

        return 0;
    }

    private static IEnumerable<(ushort Vk, char Ch, uint State)> Keys(string token)
    {
        if (token.StartsWith("text:", StringComparison.Ordinal))
        {
            foreach (var c in token[5..])
            {
                var scan = VkKeyScanW(c);
                var state = (scan & 0x100) != 0 ? Shift : 0;
                yield return ((ushort)(scan & 0xff), c, state);
            }

            yield break;
        }

        if (token.StartsWith("ctrl+", StringComparison.Ordinal) && token.Length == 6)
        {
            var letter = char.ToUpperInvariant(token[5]);
            yield return (letter, (char)(letter - 'A' + 1), LeftCtrl);
            yield break;
        }

        if (token.StartsWith("alt+", StringComparison.Ordinal) && token.Length == 5)
        {
            var c = token[4];
            yield return ((ushort)(VkKeyScanW(c) & 0xff), c, LeftAlt);
            yield break;
        }

        yield return token switch
        {
            "enter" => ((ushort)0x0D, '\r', 0u),
            "esc" => ((ushort)0x1B, '\e', 0u),
            "tab" => ((ushort)0x09, '\t', 0u),
            "bs" => ((ushort)0x08, '\b', 0u),
            "up" => ((ushort)0x26, '\0', 0x100u),
            "down" => ((ushort)0x28, '\0', 0x100u),
            "left" => ((ushort)0x25, '\0', 0x100u),
            "right" => ((ushort)0x27, '\0', 0x100u),
            _ => throw new ArgumentException($"unknown token {token}"),
        };
    }

    private static void Tap(nint input, ushort vk, char ch, uint state)
    {
        var records = stackalloc WindowsConsole.InputRecord[2];
        for (var i = 0; i < 2; i++)
        {
            records[i].EventType = WindowsConsole.KeyEvent;
            records[i].Key = new WindowsConsole.KeyEventRecord
            {
                KeyDown = i == 0 ? 1 : 0,
                RepeatCount = 1,
                VirtualKeyCode = vk,
                VirtualScanCode = (ushort)MapVirtualKeyW(vk, 0),
                UnicodeChar = ch,
                ControlKeyState = state,
            };
        }

        WriteConsoleInputW(input, records, 2, out _);
        Thread.Sleep(15);
    }

    private static void Trace(string line)
    {
        var path = Environment.GetEnvironmentVariable("EMBEDDEDSPIKE_INJECT_LOG");
        if (path is not null)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveWindow(nint hwnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint pid);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteConsoleInputW(nint handle, WindowsConsole.InputRecord* records, uint count, out uint written);

    [LibraryImport("user32.dll")]
    private static partial short VkKeyScanW(ushort c);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);
}
