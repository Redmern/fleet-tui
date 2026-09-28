using System.Runtime.InteropServices;
using System.Text;

namespace Fleet.Platform.Mux.Embedded.Host;

public static unsafe partial class HostEffectsOut
{
    public const int MaxTitle = 256;

    private const uint UnicodeText = 13;
    private const uint Moveable = 0x0002;

    public static string TitleSequence(string title) => $"\e]0;{Sanitize(title)}\a";

    public static string ClipboardSequence(string text) =>
        $"\e]52;c;{Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}\a";

    public static string Sanitize(string title)
    {
        var clean = new StringBuilder(Math.Min(title.Length, MaxTitle));
        foreach (var c in title)
        {
            if (clean.Length >= MaxTitle)
            {
                break;
            }

            if (!char.IsControl(c))
            {
                clean.Append(c);
            }
        }

        return clean.ToString();
    }

    public static string? CurrentConsoleTitle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var buffer = new char[1024];
        fixed (char* p = buffer)
        {
            var length = GetConsoleTitleW(p, (uint)buffer.Length);
            return length == 0 ? null : new string(buffer, 0, (int)length);
        }
    }

    public static void SetConsoleTitle(string title)
    {
        if (OperatingSystem.IsWindows())
        {
            SetConsoleTitleW(title);
        }
    }

    public static bool SetWindowsClipboard(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(0))
            {
                try
                {
                    EmptyClipboard();
                    var bytes = (text.Length + 1) * sizeof(char);
                    var memory = GlobalAlloc(Moveable, (nuint)bytes);
                    if (memory == 0)
                    {
                        return false;
                    }

                    var target = GlobalLock(memory);
                    fixed (char* source = text + "\0")
                    {
                        System.Buffer.MemoryCopy(source, (void*)target, bytes, bytes);
                    }

                    GlobalUnlock(memory);
                    return SetClipboardData(UnicodeText, memory) != 0;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(20);
        }

        return false;
    }

    public static string? ReadClipboard()
    {
        if (!OperatingSystem.IsWindows() || !OpenClipboard(0))
        {
            return null;
        }

        try
        {
            var memory = GetClipboardData(UnicodeText);
            if (memory == 0)
            {
                return null;
            }

            var text = GlobalLock(memory);
            try
            {
                return text == 0 ? null : new string((char*)text);
            }
            finally
            {
                GlobalUnlock(memory);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleTitleW(char* title, uint size);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleTitleW(string title);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
}
