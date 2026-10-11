using System.Runtime.InteropServices;
using Fleet.Ports;

namespace Fleet.Platform.Clipboard;

public interface IWindowsClipboard
{
    uint PngFormat { get; }

    bool Has(uint format);

    byte[]? Read(uint format, int limit);
}

public sealed class WindowsClipboardImageSource(int maxBytes, IWindowsClipboard? clipboard = null, int maxDibBytes = WindowsClipboardImageSource.DefaultMaxDibBytes)
    : IClipboardImageSource
{
    public const uint Dib = 8;
    public const uint DibV5 = 17;
    public const int DefaultMaxDibBytes = 256 * 1024 * 1024;

    private readonly IWindowsClipboard _clipboard = clipboard ?? new WindowsClipboard();

    public Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default) => Task.Run(ReadPng, ct);

    public Task<ClipboardImage> ProbePngAsync(CancellationToken ct = default) =>
        Task.Run(() => _clipboard.Has(_clipboard.PngFormat) || _clipboard.Has(DibV5) || _clipboard.Has(Dib)
            ? ClipboardImage.Available
            : ClipboardImage.None, ct);

    private ClipboardImage ReadPng()
    {
        if (_clipboard.PngFormat != 0 && _clipboard.Has(_clipboard.PngFormat)
            && _clipboard.Read(_clipboard.PngFormat, maxBytes + 1) is { Length: > 0 } png)
        {
            return new ClipboardImage(png);
        }

        foreach (var format in (ReadOnlySpan<uint>)[DibV5, Dib])
        {
            if (!_clipboard.Has(format) || _clipboard.Read(format, maxDibBytes + 1) is not { Length: > 0 } dib)
            {
                continue;
            }

            if (dib.Length > maxDibBytes)
            {
                return ClipboardImage.Refusal(
                    $"the clipboard bitmap is too large to convert: over {Mib(maxDibBytes)} MiB");
            }

            if (DibPng.Convert(dib) is { } converted)
            {
                return new ClipboardImage(converted.Length > maxBytes ? converted[..(maxBytes + 1)] : converted);
            }
        }

        return ClipboardImage.None;
    }

    private static string Mib(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed unsafe partial class WindowsClipboard : IWindowsClipboard
{
    private const int OpenAttempts = 5;

    public uint PngFormat { get; } = OperatingSystem.IsWindows() ? RegisterClipboardFormatW("PNG") : 0;

    public bool Has(uint format) => OperatingSystem.IsWindows() && format != 0 && IsClipboardFormatAvailable(format);

    public byte[]? Read(uint format, int limit)
    {
        if (!OperatingSystem.IsWindows() || format == 0 || !Open())
        {
            return null;
        }

        try
        {
            var memory = GetClipboardData(format);
            if (memory == 0)
            {
                return null;
            }

            var size = (long)Math.Min(GlobalSize(memory), (nuint)limit);
            var data = GlobalLock(memory);
            if (data == 0)
            {
                return null;
            }

            try
            {
                return new ReadOnlySpan<byte>((void*)data, (int)size).ToArray();
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

    private static bool Open()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(0))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
}
