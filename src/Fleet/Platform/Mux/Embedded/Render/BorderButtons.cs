using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Render;

public readonly record struct PlacedButton(FloatButton Button, int X, int Y, int Width);

public static class BorderButtons
{
    public const int Inset = 2;

    public const int Gap = 1;

    public static int Width(FloatButton button) => Runes(Text(button)) + 2;

    public static string Text(FloatButton button) =>
        button.Key.Length == 0 ? $" {button.Label} " : $" {button.Key} {button.Label} ";

    public static IReadOnlyList<PlacedButton> Place(Rect box, IReadOnlyList<FloatButton> buttons)
    {
        var placed = new List<PlacedButton>();

        if (buttons.Count == 0 || box.Width <= 2 * Inset || box.Height < 2)
        {
            return placed;
        }

        foreach (var bottom in (bool[])[false, true])
        {
            var y = bottom ? box.Y + box.Height - 1 : box.Y;
            var from = box.X + Inset;
            var to = box.X + box.Width - Inset;

            foreach (var button in Fitting(buttons.Where(b => b.Bottom == bottom && !b.Right), to - from))
            {
                placed.Add(new PlacedButton(button, from, y, Width(button)));
                from += Width(button) + Gap;
            }

            var right = Fitting(buttons.Where(b => b.Bottom == bottom && b.Right), to - from);
            var x = to - (right.Sum(Width) + Gap * Math.Max(0, right.Count - 1));

            foreach (var button in right)
            {
                placed.Add(new PlacedButton(button, x, y, Width(button)));
                x += Width(button) + Gap;
            }
        }

        return placed;
    }

    public static (int From, int To) TitleRoom(Rect box, IReadOnlyList<PlacedButton> placed)
    {
        var top = placed.Where(p => p.Y == box.Y).ToList();
        var from = top.Where(p => !p.Button.Right).Select(p => p.X + p.Width + Gap).DefaultIfEmpty(box.X + Inset).Max();
        var to = top.Where(p => p.Button.Right).Select(p => p.X - Gap).DefaultIfEmpty(box.X + box.Width - Inset).Min();
        return (from, to);
    }

    public static FloatButton? At(Rect box, IReadOnlyList<FloatButton> buttons, int x, int y) =>
        Place(box, buttons)
            .Where(p => p.Y == y && x >= p.X && x < p.X + p.Width)
            .Select(p => p.Button)
            .FirstOrDefault();

    public static FloatButton From(FloatButtonDto dto) => new(
        dto.Edge == "bottom",
        dto.Align == "right",
        dto.Key,
        dto.Label,
        dto.Send,
        dto.Tip ?? string.Empty);

    private static List<FloatButton> Fitting(IEnumerable<FloatButton> buttons, int room)
    {
        var fitting = new List<FloatButton>();
        var used = 0;

        foreach (var button in buttons)
        {
            var needed = Width(button) + (fitting.Count > 0 ? Gap : 0);
            if (used + needed > room)
            {
                break;
            }

            fitting.Add(button);
            used += needed;
        }

        return fitting;
    }

    private static int Runes(string text) => text.EnumerateRunes().Count();
}
