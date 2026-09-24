using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace EmbeddedSpike.Ghostty;

// One libghostty-vt terminal. Not thread-safe: every call, including the key
// encoder reading modes from it, must hold Gate.
internal sealed unsafe class VtTerminal : IDisposable
{
    private readonly GCHandle _self;
    private readonly Action<ReadOnlyMemory<byte>> _toPty;
    private nint _terminal;

    public VtTerminal(ushort cols, ushort rows, Action<ReadOnlyMemory<byte>> toPty)
    {
        _toPty = toPty;
        Check(Native.TerminalNew(0, out _terminal, cols, rows), "ghostty_terminal_new");

        _self = GCHandle.Alloc(this);
        Native.TerminalSet(_terminal, TerminalOption.Userdata, (void*)GCHandle.ToIntPtr(_self));

        delegate* unmanaged[Cdecl]<nint, nint, byte*, nuint, void> writePty = &OnWritePty;
        Native.TerminalSet(_terminal, TerminalOption.WritePty, writePty);

        delegate* unmanaged[Cdecl]<nint, nint, DeviceAttributes*, byte> da = &OnDeviceAttributes;
        Native.TerminalSet(_terminal, TerminalOption.DeviceAttributes, da);

        Cols = cols;
        Rows = rows;
    }

    public object Gate { get; } = new();

    public nint Handle => _terminal;

    public ushort Cols { get; private set; }

    public ushort Rows { get; private set; }

    public void Write(ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
        {
            Native.TerminalVtWrite(_terminal, p, (nuint)data.Length);
        }
    }

    public void Resize(ushort cols, ushort rows)
    {
        Check(Native.TerminalResize(_terminal, cols, rows, 8, 16), "ghostty_terminal_resize");
        Cols = cols;
        Rows = rows;
    }

    public bool AlternateScreen
    {
        get
        {
            int screen = 0;
            Native.TerminalGet(_terminal, TerminalData.ActiveScreen, &screen);
            return screen == 1;
        }
    }

    public string PlainText()
    {
        var options = new FormatterTerminalOptions
        {
            Size = (nuint)sizeof(FormatterTerminalOptions),
            Emit = 0,
            Unwrap = 0,
            Trim = 1,
            ExtraSize = 32,
            ScreenExtraSize = 16,
        };

        Check(Native.FormatterTerminalNew(0, out var formatter, _terminal, options), "ghostty_formatter_terminal_new");
        try
        {
            Check(Native.FormatterFormatAlloc(formatter, 0, out var ptr, out var len), "ghostty_formatter_format_alloc");
            try
            {
                return ptr == null || len == 0 ? string.Empty : Encoding.UTF8.GetString(ptr, (int)len);
            }
            finally
            {
                Native.Free(0, ptr, len);
            }
        }
        finally
        {
            Native.FormatterFree(formatter);
        }
    }

    public void Dispose()
    {
        if (_terminal != 0)
        {
            Native.TerminalFree(_terminal);
            _terminal = 0;
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    internal static void Check(int result, string what)
    {
        if (result != Native.Success)
        {
            throw new InvalidOperationException($"{what} returned {result}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWritePty(nint terminal, nint userdata, byte* data, nuint len)
    {
        if (GCHandle.FromIntPtr(userdata).Target is VtTerminal self && len > 0)
        {
            self._toPty(new ReadOnlySpan<byte>(data, (int)len).ToArray());
        }
    }

    // Answer DA1/DA2 as a VT220-class terminal with ANSI colour, the same shape
    // Ghostty itself reports. Without this callback DA queries go unanswered and
    // programs that use DA1 as a query terminator (nvim) wait for a timeout.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte OnDeviceAttributes(nint terminal, nint userdata, DeviceAttributes* attrs)
    {
        attrs->ConformanceLevel = 62;
        attrs->Features[0] = 22;
        attrs->NumFeatures = 1;
        attrs->DeviceType = 1;
        attrs->FirmwareVersion = 10;
        attrs->RomCartridge = 0;
        attrs->UnitId = 0;
        return 1;
    }
}
