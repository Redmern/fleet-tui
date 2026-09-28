using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Platform.Mux.Embedded.Native;

public sealed unsafe class GhosttyTerminal : IPaneTerminal
{
    private readonly GCHandle _self;
    private readonly Action<byte[]> _reply;
    private readonly byte[] _utf8 = new byte[64];
    private readonly byte[] _keyBuffer = new byte[128];
    private nint _terminal;
    private nint _state;
    private nint _rows;
    private nint _cells;
    private const uint CellWidth = 8;
    private const uint CellHeight = 16;

    private nint _encoder;
    private nint _event;
    private nint _mouseEncoder;
    private nint _mouseEvent;
    private bool _resized = true;

    public GhosttyTerminal(int cols, int rows, Action<byte[]> reply)
    {
        _reply = reply;
        Check(GhosttyNative.TerminalNew(0, out _terminal, (ushort)cols, (ushort)rows), "ghostty_terminal_new");
        _self = GCHandle.Alloc(this);
        GhosttyNative.TerminalSet(_terminal, TerminalOption.Userdata, (void*)GCHandle.ToIntPtr(_self));

        delegate* unmanaged[Cdecl]<nint, nint, byte*, nuint, void> writePty = &OnWritePty;
        GhosttyNative.TerminalSet(_terminal, TerminalOption.WritePty, writePty);

        delegate* unmanaged[Cdecl]<nint, nint, DeviceAttributes*, byte> da = &OnDeviceAttributes;
        GhosttyNative.TerminalSet(_terminal, TerminalOption.DeviceAttributes, da);

        Check(GhosttyNative.RenderStateNew(0, out _state), "ghostty_render_state_new");
        Check(GhosttyNative.RowIteratorNew(0, out _rows), "ghostty_render_state_row_iterator_new");
        Check(GhosttyNative.RowCellsNew(0, out _cells), "ghostty_render_state_row_cells_new");
        Check(GhosttyNative.KeyEncoderNew(0, out _encoder), "ghostty_key_encoder_new");
        Check(GhosttyNative.KeyEventNew(0, out _event), "ghostty_key_event_new");
    }

    public static IPaneTerminal Create(int cols, int rows, Action<byte[]> reply) =>
        new GhosttyTerminal(cols, rows, reply);

    public void Write(ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
        {
            GhosttyNative.TerminalVtWrite(_terminal, p, (nuint)data.Length);
        }
    }

    public void Resize(int cols, int rows)
    {
        Check(GhosttyNative.TerminalResize(_terminal, (ushort)cols, (ushort)rows, CellWidth, CellHeight), "ghostty_terminal_resize");
        _resized = true;
    }

    public bool Snapshot(ScreenBuffer screen)
    {
        Check(GhosttyNative.RenderStateUpdate(_state, _terminal), "ghostty_render_state_update");

        ushort cols = 0;
        ushort rows = 0;
        GhosttyNative.RenderStateGet(_state, RenderStateData.Cols, &cols);
        GhosttyNative.RenderStateGet(_state, RenderStateData.Rows, &rows);

        var full = _resized || screen.Cols != cols || screen.Rows != rows;
        screen.Resize(cols, rows);
        _resized = false;

        var dirty = RenderStateDirty.False;
        GhosttyNative.RenderStateGet(_state, RenderStateData.Dirty, &dirty);
        full |= dirty == RenderStateDirty.Full;

        var changed = full || dirty != RenderStateDirty.False;
        var iterator = _rows;
        Check(GhosttyNative.RenderStateGet(_state, RenderStateData.RowIterator, &iterator), "row iterator");

        var y = 0;
        while (GhosttyNative.RowIteratorNext(_rows) && y < rows)
        {
            byte rowDirty = 0;
            GhosttyNative.RowGet(_rows, RowData.Dirty, &rowDirty);

            if (full || rowDirty != 0)
            {
                ReadRow(screen.Row(y));
                byte clean = 0;
                GhosttyNative.RowSet(_rows, RowOption.Dirty, &clean);
            }

            y++;
        }

        var notDirty = RenderStateDirty.False;
        GhosttyNative.RenderStateSet(_state, RenderStateOption.Dirty, &notDirty);

        var cursor = default(RenderCursor);
        cursor.Size = (nuint)sizeof(RenderCursor);
        GhosttyNative.RenderStateGet(_state, RenderStateData.Cursor, &cursor);

        var visible = cursor.Visible != 0 && cursor.ViewportHasValue != 0;
        var shape = (cursor.VisualStyle switch { 0 => 5, 2 => 3, _ => 1 }) + (cursor.Blinking == 0 ? 1 : 0);

        if (screen.CursorX != cursor.ViewportX || screen.CursorY != cursor.ViewportY
            || screen.CursorVisible != visible || screen.CursorShape != shape)
        {
            changed = true;
        }

        screen.CursorX = cursor.ViewportX;
        screen.CursorY = cursor.ViewportY;
        screen.CursorVisible = visible;
        screen.CursorShape = shape;

        if (changed)
        {
            screen.Version++;
        }

        return changed;
    }

    public byte[] Encode(KeyMessage key)
    {
        GhosttyNative.KeyEncoderSetoptFromTerminal(_encoder, _terminal);
        GhosttyNative.KeyEventSetAction(_event, (KeyAction)key.Action);
        GhosttyNative.KeyEventSetKey(_event, (Key)key.Key);
        GhosttyNative.KeyEventSetMods(_event, (ushort)key.Mods);
        GhosttyNative.KeyEventSetConsumedMods(_event, (ushort)key.Consumed);
        GhosttyNative.KeyEventSetComposing(_event, false);
        GhosttyNative.KeyEventSetUnshiftedCodepoint(_event, key.Unshifted);

        var utf8 = key.Text is null ? [] : Encoding.UTF8.GetBytes(key.Text);
        fixed (byte* text = utf8)
        {
            GhosttyNative.KeyEventSetUtf8(_event, utf8.Length == 0 ? null : text, (nuint)utf8.Length);

            fixed (byte* output = _keyBuffer)
            {
                var rc = GhosttyNative.KeyEncoderEncode(_encoder, _event, output, (nuint)_keyBuffer.Length, out var written);
                return rc == GhosttyNative.Success ? _keyBuffer.AsSpan(0, (int)written).ToArray() : [];
            }
        }
    }

    public byte[] EncodeMouse(MouseMessage mouse, int x, int y)
    {
        if (_mouseEncoder == 0)
        {
            Check(GhosttyNative.MouseEncoderNew(0, out _mouseEncoder), "ghostty_mouse_encoder_new");
            Check(GhosttyNative.MouseEventNew(0, out _mouseEvent), "ghostty_mouse_event_new");
            byte on = 1;
            GhosttyNative.MouseEncoderSetopt(_mouseEncoder, MouseEncoderOption.TrackLastCell, &on);
        }

        ushort cols = 0;
        ushort rows = 0;
        GhosttyNative.TerminalGet(_terminal, TerminalData.Cols, &cols);
        GhosttyNative.TerminalGet(_terminal, TerminalData.Rows, &rows);

        GhosttyNative.MouseEncoderSetoptFromTerminal(_mouseEncoder, _terminal);

        var size = new MouseEncoderSize
        {
            Size = (nuint)sizeof(MouseEncoderSize),
            ScreenWidth = (uint)(cols * CellWidth),
            ScreenHeight = (uint)(rows * CellHeight),
            CellWidth = CellWidth,
            CellHeight = CellHeight,
        };
        GhosttyNative.MouseEncoderSetopt(_mouseEncoder, MouseEncoderOption.Size, &size);

        byte held = mouse.Held ? (byte)1 : (byte)0;
        GhosttyNative.MouseEncoderSetopt(_mouseEncoder, MouseEncoderOption.AnyButtonPressed, &held);

        GhosttyNative.MouseEventSetAction(_mouseEvent, mouse.Action);
        if (mouse.Button == Protocol.MouseButtons.None)
        {
            GhosttyNative.MouseEventClearButton(_mouseEvent);
        }
        else
        {
            GhosttyNative.MouseEventSetButton(_mouseEvent, mouse.Button);
        }

        GhosttyNative.MouseEventSetMods(_mouseEvent, (ushort)mouse.Mods);
        GhosttyNative.MouseEventSetPosition(_mouseEvent, new MousePosition
        {
            X = x * CellWidth + CellWidth / 2f,
            Y = y * CellHeight + CellHeight / 2f,
        });

        fixed (byte* output = _keyBuffer)
        {
            var rc = GhosttyNative.MouseEncoderEncode(_mouseEncoder, _mouseEvent, output, (nuint)_keyBuffer.Length, out var written);
            return rc == GhosttyNative.Success ? _keyBuffer.AsSpan(0, (int)written).ToArray() : [];
        }
    }

    public string PlainText()
    {
        var options = new FormatterTerminalOptions
        {
            Size = (nuint)sizeof(FormatterTerminalOptions),
            Emit = 0,
            Trim = 1,
            ExtraSize = 32,
            ScreenExtraSize = 16,
        };

        Check(GhosttyNative.FormatterTerminalNew(0, out var formatter, _terminal, options), "ghostty_formatter_terminal_new");
        try
        {
            Check(GhosttyNative.FormatterFormatAlloc(formatter, 0, out var ptr, out var len), "ghostty_formatter_format_alloc");
            try
            {
                return ptr == null || len == 0 ? string.Empty : Encoding.UTF8.GetString(ptr, (int)len);
            }
            finally
            {
                GhosttyNative.Free(0, ptr, len);
            }
        }
        finally
        {
            GhosttyNative.FormatterFree(formatter);
        }
    }

    public void Dispose()
    {
        if (_terminal == 0)
        {
            return;
        }

        GhosttyNative.KeyEventFree(_event);
        GhosttyNative.KeyEncoderFree(_encoder);

        if (_mouseEncoder != 0)
        {
            GhosttyNative.MouseEventFree(_mouseEvent);
            GhosttyNative.MouseEncoderFree(_mouseEncoder);
        }
        GhosttyNative.RowCellsFree(_cells);
        GhosttyNative.RowIteratorFree(_rows);
        GhosttyNative.RenderStateFree(_state);
        GhosttyNative.TerminalFree(_terminal);
        _terminal = 0;

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private void ReadRow(Span<Cell> row)
    {
        var cells = _cells;
        Check(GhosttyNative.RowGet(_rows, RowData.Cells, &cells), "row cells");

        var x = 0;
        while (GhosttyNative.RowCellsNext(_cells) && x < row.Length)
        {
            row[x++] = ReadCell();
        }

        while (x < row.Length)
        {
            row[x++] = Cell.Blank;
        }
    }

    private Cell ReadCell()
    {
        ulong raw = 0;
        GhosttyNative.RowCellsGet(_cells, CellsData.Raw, &raw);

        var wide = CellWide.Narrow;
        GhosttyNative.CellGet(raw, CellData.Wide, &wide);

        if (wide == CellWide.SpacerTail)
        {
            return Cell.WideTail;
        }

        var style = default(Style);
        style.Size = (nuint)sizeof(Style);
        GhosttyNative.RowCellsGet(_cells, CellsData.Style, &style);

        string text;
        fixed (byte* p = _utf8)
        {
            var buffer = new GhosttyBuffer { Ptr = p, Cap = (nuint)_utf8.Length };
            var result = GhosttyNative.RowCellsGet(_cells, CellsData.GraphemesUtf8, &buffer);
            text = result == GhosttyNative.Success && buffer.Len > 0
                ? Encoding.UTF8.GetString(p, (int)buffer.Len)
                : " ";
        }

        var fg = Color(style.Fg);
        var bg = Color(style.Bg);

        if (bg == Cell.Default)
        {
            Rgb rgb;
            if (GhosttyNative.RowCellsGet(_cells, CellsData.BgColor, &rgb) == GhosttyNative.Success)
            {
                bg = Cell.Rgb(rgb.R, rgb.G, rgb.B);
            }
        }

        var attrs = CellAttr.None;
        if (style.Bold != 0) attrs |= CellAttr.Bold;
        if (style.Faint != 0) attrs |= CellAttr.Faint;
        if (style.Italic != 0) attrs |= CellAttr.Italic;
        if (style.UnderlineKind != 0) attrs |= CellAttr.Underline;
        if (style.Blink != 0) attrs |= CellAttr.Blink;
        if (style.Inverse != 0) attrs |= CellAttr.Inverse;
        if (style.Invisible != 0) attrs |= CellAttr.Invisible;
        if (style.Strikethrough != 0) attrs |= CellAttr.Strike;
        if (style.Overline != 0) attrs |= CellAttr.Overline;

        return new Cell(text, fg, bg, attrs, (byte)style.UnderlineKind, wide == CellWide.Wide);
    }

    private static uint Color(StyleColor color) => color.Tag switch
    {
        StyleColorTag.Palette => Cell.Palette(color.Palette),
        StyleColorTag.Rgb => Cell.Rgb(color.Rgb.R, color.Rgb.G, color.Rgb.B),
        _ => Cell.Default,
    };

    private static void Check(int result, string what)
    {
        if (result != GhosttyNative.Success)
        {
            throw new InvalidOperationException($"{what} returned {result}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWritePty(nint terminal, nint userdata, byte* data, nuint len)
    {
        if (GCHandle.FromIntPtr(userdata).Target is GhosttyTerminal self && len > 0)
        {
            self._reply(new ReadOnlySpan<byte>(data, (int)len).ToArray());
        }
    }

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
