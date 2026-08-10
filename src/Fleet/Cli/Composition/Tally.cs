namespace Fleet.Cli.Composition;

public static class Tally
{
    public static (int Left, int Right) Parse(string counts)
    {
        var parts = counts.Split(
            ['	', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2
            || !int.TryParse(parts[0], out var left)
            || !int.TryParse(parts[1], out var right))
        {
            return (0, 0);
        }

        return (left, right);
    }
}
