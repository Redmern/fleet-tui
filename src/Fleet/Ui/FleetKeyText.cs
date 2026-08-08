namespace Fleet.Ui;

public static class FleetKeyText
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CursorDown"] = "down",
        ["CursorUp"] = "up",
        ["CursorLeft"] = "left",
        ["CursorRight"] = "right",
        ["PageDown"] = "pgdn",
        ["PageUp"] = "pgup",
        ["Backspace"] = "bksp",
    };

    public static string Display(string keyText)
    {
        if (string.IsNullOrWhiteSpace(keyText))
        {
            return string.Empty;
        }

        var parts = keyText.Split('+', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (parts.Length == 1)
        {
            return Bare(parts[0]);
        }

        return string.Join("+", parts.Select(Modified));
    }

    private static string Bare(string part)
        => Aliases.TryGetValue(part, out var alias)
            ? alias
            : part.Length == 1 ? part : part.ToLowerInvariant();

    private static string Modified(string part)
        => Aliases.TryGetValue(part, out var alias) ? alias : part.ToLowerInvariant();
}
