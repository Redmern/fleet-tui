using System.Text;
using EmbeddedSpike.Ghostty;

namespace EmbeddedSpike.Render;

// Paints the emulator's grid onto the host terminal by diffing against a shadow
// copy of what the host is showing. Only rows the emulator marks dirty are read,
// and within those only cells that differ from the shadow are written.
//
// Colours are re-emitted as the application asked for them: palette indices stay
// palette indices (so the host's theme applies), RGB stays RGB, and "no colour"
// becomes SGR 39/49 so the host's own default fg/bg show through.
internal sealed unsafe class GridRenderer : IDisposable
{
    private readonly VtTerminal _terminal;
    private readonly nint _state;
    private readonly nint _rows;
    private readonly nint _cells;
    private readonly StringBuilder _out = new(64 * 1024);
    private readonly byte[] _utf8 = new byte[64];

    private Cell[] _shadow = [];
    private int _cols;
    private int _rowCount;
    private bool _forceFull = true;
    private string? _badge;
    private int _badgeRow = -1;

    public GridRenderer(VtTerminal terminal)
    {
        _terminal = terminal;
        VtTerminal.Check(Native.RenderStateNew(0, out _state), "ghostty_render_state_new");
        VtTerminal.Check(Native.RowIteratorNew(0, out _rows), "ghostty_render_state_row_iterator_new");
        VtTerminal.Check(Native.RowCellsNew(0, out _cells), "ghostty_render_state_row_cells_new");
    }

    public long CellsWritten { get; private set; }

    public long Frames { get; private set; }

    public void Invalidate() => _forceFull = true;

    public void SetBadge(string? badge)
    {
        if (badge == _badge)
        {
            return;
        }

        _badge = badge;
        if (_badgeRow >= 0 && _badgeRow < _rowCount)
        {
            ForgetRow(_badgeRow);
        }
    }

    // Must be called holding the terminal's Gate. Returns the bytes to write.
    public string Frame()
    {
        _out.Clear();
        VtTerminal.Check(Native.RenderStateUpdate(_state, _terminal.Handle), "ghostty_render_state_update");

        ushort cols = 0;
        ushort rows = 0;
        Native.RenderStateGet(_state, RenderStateData.Cols, &cols);
        Native.RenderStateGet(_state, RenderStateData.Rows, &rows);

        if (cols != _cols || rows != _rowCount)
        {
            _cols = cols;
            _rowCount = rows;
            _shadow = new Cell[cols * rows];
            _forceFull = true;
        }

        var dirty = RenderStateDirty.False;
        Native.RenderStateGet(_state, RenderStateData.Dirty, &dirty);
        var full = _forceFull || dirty == RenderStateDirty.Full;

        if (_forceFull)
        {
            _out.Append("\e[0m\e[2J");
            Array.Fill(_shadow, Cell.Unknown);
            _forceFull = false;
        }

        _out.Append("\e[?2026h\e[?25l");

        var iterator = _rows;
        VtTerminal.Check(Native.RenderStateGet(_state, RenderStateData.RowIterator, &iterator), "row iterator");

        var pen = Cell.Unknown;
        var y = 0;
        var row = new Cell[cols];

        while (Native.RowIteratorNext(_rows) && y < rows)
        {
            byte rowDirty = 0;
            Native.RowGet(_rows, RowData.Dirty, &rowDirty);

            if (full || rowDirty != 0 || y == _badgeRow)
            {
                ReadRow(row);
                Overlay(row, y, rows);
                pen = DiffRow(row, y, pen);

                byte clean = 0;
                Native.RowSet(_rows, RowOption.Dirty, &clean);
            }

            y++;
        }

        var notDirty = RenderStateDirty.False;
        Native.RenderStateSet(_state, RenderStateOption.Dirty, &notDirty);

        PlaceCursor();
        _out.Append("\e[?2026l");
        Frames++;
        return _out.ToString();
    }

    private void ReadRow(Cell[] row)
    {
        var cells = _cells;
        VtTerminal.Check(Native.RowGet(_rows, RowData.Cells, &cells), "row cells");

        var x = 0;
        while (Native.RowCellsNext(_cells) && x < row.Length)
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
        Native.RowCellsGet(_cells, CellsData.Raw, &raw);

        var wide = CellWide.Narrow;
        Native.CellGet(raw, CellData.Wide, &wide);

        if (wide == CellWide.SpacerTail)
        {
            return Cell.WideTail;
        }

        var style = default(Style);
        style.Size = (nuint)sizeof(Style);
        Native.RowCellsGet(_cells, CellsData.Style, &style);

        string text;
        fixed (byte* p = _utf8)
        {
            var buffer = new Ghostty.Buffer { Ptr = p, Cap = (nuint)_utf8.Length };
            var result = Native.RowCellsGet(_cells, CellsData.GraphemesUtf8, &buffer);
            text = result == Native.Success && buffer.Len > 0
                ? Encoding.UTF8.GetString(p, (int)buffer.Len)
                : " ";
        }

        var fg = EncodeColor(style.Fg);
        var bg = EncodeColor(style.Bg);

        if (bg == 0)
        {
            Rgb rgb;
            if (Native.RowCellsGet(_cells, CellsData.BgColor, &rgb) == Native.Success)
            {
                bg = 0x0200_0000u | ((uint)rgb.R << 16) | ((uint)rgb.G << 8) | rgb.B;
            }
        }

        var attrs = Attr.None;
        if (style.Bold != 0) attrs |= Attr.Bold;
        if (style.Faint != 0) attrs |= Attr.Faint;
        if (style.Italic != 0) attrs |= Attr.Italic;
        if (style.UnderlineKind != 0) attrs |= Attr.Underline;
        if (style.Blink != 0) attrs |= Attr.Blink;
        if (style.Inverse != 0) attrs |= Attr.Inverse;
        if (style.Invisible != 0) attrs |= Attr.Invisible;
        if (style.Strikethrough != 0) attrs |= Attr.Strike;
        if (style.Overline != 0) attrs |= Attr.Overline;

        return new Cell(text, fg, bg, attrs, (byte)style.UnderlineKind, wide == CellWide.Wide);
    }

    // 0 = default, 0x01_0000nn = palette n, 0x02_rrggbb = truecolour.
    private static uint EncodeColor(StyleColor color) => color.Tag switch
    {
        StyleColorTag.Palette => 0x0100_0000u | color.Palette,
        StyleColorTag.Rgb => 0x0200_0000u | ((uint)color.Rgb.R << 16) | ((uint)color.Rgb.G << 8) | color.Rgb.B,
        _ => 0,
    };

    private void Overlay(Cell[] row, int y, int rows)
    {
        if (_badge is null)
        {
            _badgeRow = -1;
            return;
        }

        _badgeRow = 0;
        if (y != _badgeRow)
        {
            return;
        }

        var start = Math.Max(0, row.Length - _badge.Length);
        for (var i = 0; i < _badge.Length && start + i < row.Length; i++)
        {
            row[start + i] = new Cell(_badge[i].ToString(), 0x0100_0000u | 0, 0x0100_0000u | 11, Attr.Bold, 0, false);
        }

        if (start > 0 && row[start - 1].Wide)
        {
            row[start - 1] = Cell.Blank;
        }
    }

    private Cell DiffRow(Cell[] row, int y, Cell pen)
    {
        var baseIndex = y * _cols;
        var cursorX = -1;

        for (var x = 0; x < row.Length; x++)
        {
            var cell = row[x];
            ref var shown = ref _shadow[baseIndex + x];

            if (cell.Equals(shown))
            {
                continue;
            }

            shown = cell;

            if (cell.IsWideTail)
            {
                continue;
            }

            if (cursorX != x)
            {
                _out.Append("\e[").Append(y + 1).Append(';').Append(x + 1).Append('H');
            }

            if (!cell.SameStyle(pen))
            {
                AppendSgr(cell);
                pen = cell;
            }

            _out.Append(cell.Text);
            CellsWritten++;
            cursorX = x + (cell.Wide ? 2 : 1);

            if (cell.Wide && x + 1 < row.Length)
            {
                _shadow[baseIndex + x + 1] = row[x + 1];
                x++;
            }
        }

        return pen;
    }

    private void AppendSgr(Cell cell)
    {
        _out.Append("\e[0");
        if ((cell.Attrs & Attr.Bold) != 0) _out.Append(";1");
        if ((cell.Attrs & Attr.Faint) != 0) _out.Append(";2");
        if ((cell.Attrs & Attr.Italic) != 0) _out.Append(";3");
        if ((cell.Attrs & Attr.Underline) != 0)
        {
            _out.Append(cell.UnderlineKind is > 1 and <= 5 ? $";4:{cell.UnderlineKind}" : ";4");
        }

        if ((cell.Attrs & Attr.Blink) != 0) _out.Append(";5");
        if ((cell.Attrs & Attr.Inverse) != 0) _out.Append(";7");
        if ((cell.Attrs & Attr.Invisible) != 0) _out.Append(";8");
        if ((cell.Attrs & Attr.Strike) != 0) _out.Append(";9");
        if ((cell.Attrs & Attr.Overline) != 0) _out.Append(";53");
        AppendColor(cell.Fg, 30, 90, 38);
        AppendColor(cell.Bg, 40, 100, 48);
        _out.Append('m');
    }

    private void AppendColor(uint color, int low, int high, int extended)
    {
        var kind = color >> 24;
        if (kind == 1)
        {
            var index = (int)(color & 0xff);
            if (index < 8)
            {
                _out.Append(';').Append(low + index);
            }
            else if (index < 16)
            {
                _out.Append(';').Append(high + index - 8);
            }
            else
            {
                _out.Append(';').Append(extended).Append(";5;").Append(index);
            }
        }
        else if (kind == 2)
        {
            _out.Append(';').Append(extended).Append(";2;")
                .Append((color >> 16) & 0xff).Append(';')
                .Append((color >> 8) & 0xff).Append(';')
                .Append(color & 0xff);
        }
    }

    private void PlaceCursor()
    {
        var cursor = default(RenderCursor);
        cursor.Size = (nuint)sizeof(RenderCursor);
        Native.RenderStateGet(_state, RenderStateData.Cursor, &cursor);

        _out.Append("\e[0m");

        if (cursor.ViewportHasValue != 0)
        {
            _out.Append("\e[").Append(cursor.ViewportY + 1).Append(';').Append(cursor.ViewportX + 1).Append('H');
        }

        var shape = cursor.VisualStyle switch
        {
            0 => 5,
            2 => 3,
            _ => 1,
        };
        if (cursor.Blinking == 0)
        {
            shape++;
        }

        _out.Append("\e[").Append(shape).Append(" q");

        if (cursor.Visible != 0 && cursor.ViewportHasValue != 0)
        {
            _out.Append("\e[?25h");
        }
    }

    private void ForgetRow(int y)
    {
        for (var x = 0; x < _cols; x++)
        {
            _shadow[y * _cols + x] = Cell.Unknown;
        }
    }

    public void Dispose()
    {
        Native.RowCellsFree(_cells);
        Native.RowIteratorFree(_rows);
        Native.RenderStateFree(_state);
    }

    [Flags]
    private enum Attr : ushort
    {
        None = 0,
        Bold = 1,
        Faint = 2,
        Italic = 4,
        Underline = 8,
        Blink = 16,
        Inverse = 32,
        Invisible = 64,
        Strike = 128,
        Overline = 256,
    }

    private readonly record struct Cell(string Text, uint Fg, uint Bg, Attr Attrs, byte UnderlineKind, bool Wide)
    {
        public static readonly Cell Unknown = new("\0", uint.MaxValue, uint.MaxValue, Attr.None, 0, false);
        public static readonly Cell Blank = new(" ", 0, 0, Attr.None, 0, false);
        public static readonly Cell WideTail = new(string.Empty, 0, 0, Attr.None, 0, false);

        public bool IsWideTail => Text.Length == 0;

        public bool SameStyle(Cell other) =>
            Fg == other.Fg && Bg == other.Bg && Attrs == other.Attrs && UnderlineKind == other.UnderlineKind;
    }
}
