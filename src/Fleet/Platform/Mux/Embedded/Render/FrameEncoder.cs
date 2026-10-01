using System.Text;

namespace Fleet.Platform.Mux.Embedded.Render;

public static class FrameEncoder
{
    public static string Encode(ClientFrame? shown, ClientFrame next)
    {
        var full = shown is null || shown.Cols != next.Cols || shown.Rows != next.Rows;
        var output = new StringBuilder(full ? next.Cols * next.Rows * 2 : 1024);

        output.Append("\e[?2026h\e[?25l");

        if (full)
        {
            output.Append("\e[0m\e[H\e[2J");
        }

        var pen = Cell.Unknown;

        for (var y = 0; y < next.Rows; y++)
        {
            var cursorX = -1;

            for (var x = 0; x < next.Cols; x++)
            {
                var cell = next.At(x, y);

                if (!full && cell.Equals(shown!.At(x, y)))
                {
                    continue;
                }

                if (cell.IsWideTail)
                {
                    continue;
                }

                if (cursorX != x)
                {
                    output.Append("\e[").Append(y + 1).Append(';').Append(x + 1).Append('H');
                }

                if (!cell.SameStyle(pen))
                {
                    Sgr(output, cell);
                    pen = cell;
                }

                output.Append(cell.Text.Length == 0 || cell.Text == "\0" ? " " : cell.Text);
                cursorX = cell.Wide || WidthIsDisputed(cell.Text) ? -1 : x + 1;

                if (cell.Wide)
                {
                    x++;
                }
            }
        }

        output.Append("\e[0m\e[").Append(next.CursorY + 1).Append(';').Append(next.CursorX + 1).Append('H');
        output.Append("\e[").Append(next.CursorShape).Append(" q");

        if (next.CursorVisible)
        {
            output.Append("\e[?25h");
        }

        output.Append("\e[?2026l");
        return output.ToString();
    }

    public static bool WidthIsDisputed(string text)
    {
        if (text.Length == 0 || !char.IsSurrogate(text[0]) && text[0] < 0xE000)
        {
            return false;
        }

        var rune = System.Text.Rune.GetRuneAt(text, 0).Value;
        return rune is >= 0xE000 and <= 0xF8FF or >= 0x1F000 or >= 0xF0000;
    }

    private static void Sgr(StringBuilder output, Cell cell)
    {
        output.Append("\e[0");
        if ((cell.Attrs & CellAttr.Bold) != 0) output.Append(";1");
        if ((cell.Attrs & CellAttr.Faint) != 0) output.Append(";2");
        if ((cell.Attrs & CellAttr.Italic) != 0) output.Append(";3");
        if ((cell.Attrs & CellAttr.Underline) != 0)
        {
            output.Append(cell.UnderlineKind is > 1 and <= 5 ? $";4:{cell.UnderlineKind}" : ";4");
        }

        if ((cell.Attrs & CellAttr.Blink) != 0) output.Append(";5");
        if ((cell.Attrs & CellAttr.Inverse) != 0) output.Append(";7");
        if ((cell.Attrs & CellAttr.Invisible) != 0) output.Append(";8");
        if ((cell.Attrs & CellAttr.Strike) != 0) output.Append(";9");
        if ((cell.Attrs & CellAttr.Overline) != 0) output.Append(";53");
        Color(output, cell.Fg, 30, 90, 38);
        Color(output, cell.Bg, 40, 100, 48);
        output.Append('m');
    }

    private static void Color(StringBuilder output, uint color, int low, int high, int extended)
    {
        var kind = color >> 24;

        if (kind == 1)
        {
            var index = (int)(color & 0xff);
            if (index < 8)
            {
                output.Append(';').Append(low + index);
            }
            else if (index < 16)
            {
                output.Append(';').Append(high + index - 8);
            }
            else
            {
                output.Append(';').Append(extended).Append(";5;").Append(index);
            }
        }
        else if (kind == 2)
        {
            output.Append(';').Append(extended).Append(";2;")
                .Append((color >> 16) & 0xff).Append(';')
                .Append((color >> 8) & 0xff).Append(';')
                .Append(color & 0xff);
        }
    }
}
