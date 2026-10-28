namespace Fleet.Ui;

public static class FleetText
{
    public const string Ellipsis = "…";

    public static string Middle(string text, int width)
    {
        if (text.Length <= width)
        {
            return text;
        }

        if (width <= 0)
        {
            return string.Empty;
        }

        var kept = width - Ellipsis.Length;
        var tail = kept / 3;
        var head = kept - tail;

        return text[..head] + Ellipsis + text[^tail..];
    }

    public static IReadOnlyList<string> Lines(string text, int width, int most)
    {
        if (width <= 0 || most <= 0)
        {
            return [];
        }

        var lines = new List<string>();
        var rest = text;

        while (rest.Length > 0 && lines.Count < most - 1 && rest.Length > width)
        {
            lines.Add(rest[..width]);
            rest = rest[width..];
        }

        if (rest.Length > 0)
        {
            lines.Add(Middle(rest, width));
        }

        return lines;
    }
}
