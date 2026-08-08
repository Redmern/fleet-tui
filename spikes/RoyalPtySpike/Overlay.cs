using System.Text;

namespace RoyalPtySpike;

public static class Overlay
{
    public static string Panel(int cols, int rows)
    {
        var width = Math.Min(cols - 2, 62);

        var lines = new[]
        {
            " fleet menu (royal pty spike)",
            string.Empty,
            "   a   Add repository",
            "   r   Refresh",
            "   k   Keybinds",
            "   q   Close pane",
            string.Empty,
            "   any key dismisses, then the child repaints",
        };

        var top = Math.Max((rows - lines.Length - 2) / 2, 1);
        var left = Math.Max((cols - width) / 2, 1);

        var sb = new StringBuilder();
        sb.Append("\u001b[2J");
        sb.Append("\u001b[48;2;30;30;46m\u001b[38;2;205;214;244m");
        sb.Append($"\u001b[{top};{left}H");
        sb.Append('\u256d').Append(new string('\u2500', width - 2)).Append('\u256e');

        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].Length > width - 2 ? lines[i][..(width - 2)] : lines[i];
            sb.Append($"\u001b[{top + i + 1};{left}H");
            sb.Append('\u2502').Append(text.PadRight(width - 2)).Append('\u2502');
        }

        sb.Append($"\u001b[{top + lines.Length + 1};{left}H");
        sb.Append('\u2570').Append(new string('\u2500', width - 2)).Append('\u256f');
        sb.Append("\u001b[0m");

        return sb.ToString();
    }
}
