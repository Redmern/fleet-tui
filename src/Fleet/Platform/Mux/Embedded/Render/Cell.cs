namespace Fleet.Platform.Mux.Embedded.Render;

[Flags]
public enum CellAttr : ushort
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

public readonly record struct Cell(string Text, uint Fg, uint Bg, CellAttr Attrs, byte UnderlineKind, bool Wide)
{
    public const uint Default = 0;

    public static readonly Cell Unknown = new("\0", uint.MaxValue, uint.MaxValue, CellAttr.None, 0, false);

    public static readonly Cell Blank = new(" ", Default, Default, CellAttr.None, 0, false);

    public static readonly Cell WideTail = new(string.Empty, Default, Default, CellAttr.None, 0, false);

    public bool IsWideTail => Text.Length == 0;

    public static uint Palette(int index) => 0x0100_0000u | (uint)(index & 0xff);

    public static uint Rgb(byte r, byte g, byte b) => 0x0200_0000u | ((uint)r << 16) | ((uint)g << 8) | b;

    public static Cell Of(char c, uint fg = Default, uint bg = Default, CellAttr attrs = CellAttr.None) =>
        new(c.ToString(), fg, bg, attrs, 0, false);

    public bool SameStyle(Cell other) =>
        Fg == other.Fg && Bg == other.Bg && Attrs == other.Attrs && UnderlineKind == other.UnderlineKind;
}

public sealed class ScreenBuffer
{
    private Cell[] _cells = [];

    public int Cols { get; private set; }

    public int Rows { get; private set; }

    public int CursorX { get; set; }

    public int CursorY { get; set; }

    public bool CursorVisible { get; set; } = true;

    public int CursorShape { get; set; } = 2;

    public long Version { get; set; }

    public ReadOnlySpan<Cell> Cells => _cells;

    public void Resize(int cols, int rows)
    {
        if (cols == Cols && rows == Rows)
        {
            return;
        }

        Cols = cols;
        Rows = rows;
        _cells = new Cell[cols * rows];
        Array.Fill(_cells, Cell.Blank);
        Version++;
    }

    public Cell At(int x, int y) => _cells[y * Cols + x];

    public Span<Cell> Row(int y) => _cells.AsSpan(y * Cols, Cols);

    public void Write(int x, int y, string text, uint fg = Cell.Default, uint bg = Cell.Default)
    {
        foreach (var c in text)
        {
            if (x >= Cols || y >= Rows)
            {
                return;
            }

            _cells[y * Cols + x++] = Cell.Of(c, fg, bg);
        }

        Version++;
    }
}

public sealed class ClientFrame(int cols, int rows)
{
    public int Cols { get; } = cols;

    public int Rows { get; } = rows;

    public Cell[] Cells { get; } = Filled(cols * rows);

    public int CursorX { get; set; }

    public int CursorY { get; set; }

    public bool CursorVisible { get; set; }

    public int CursorShape { get; set; } = 2;

    public Cell At(int x, int y) => Cells[y * Cols + x];

    public string RowText(int y) =>
        string.Concat(Cells.AsSpan(y * Cols, Cols).ToArray().Select(c => c.IsWideTail ? string.Empty : c.Text));

    private static Cell[] Filled(int n)
    {
        var cells = new Cell[n];
        Array.Fill(cells, Cell.Blank);
        return cells;
    }
}
