namespace Fleet.Shared.Keybinds;

public static class NvimChord
{
    private static readonly IReadOnlyDictionary<string, string> Modifiers = new Dictionary<string, string>
    {
        ["Ctrl"] = "C",
        ["Alt"] = "A",
        ["Shift"] = "S",
        ["Super"] = "D",
    };

    private static readonly IReadOnlyDictionary<string, string> NamedKeys = new Dictionary<string, string>
    {
        ["Enter"] = "CR",
        ["Escape"] = "Esc",
        ["Backspace"] = "BS",
        ["Delete"] = "Del",
        ["PageUp"] = "PageUp",
        ["PageDown"] = "PageDown",
        ["<"] = "lt",
        ["\\"] = "Bslash",
        ["|"] = "Bar",
    };

    public static string Lhs(string chord) =>
        string.Concat(KeybindChord.Normalize(chord).Split(' ').Select(Step));

    private static string Step(string step)
    {
        var parts = step == "+" ? ["+"]
            : step.EndsWith("++", StringComparison.Ordinal) ? [.. step[..^2].Split('+'), "+"]
            : step.Split('+');
        var key = parts[^1];
        var modifiers = parts[..^1].Select(m => Modifiers[m]).ToArray();
        var named = NamedKeys.TryGetValue(key, out var name) ? name : key.Length > 1 ? key : null;

        if (modifiers.Length == 0)
        {
            return named is null ? key : $"<{named}>";
        }

        return $"<{string.Join('-', modifiers)}-{named ?? key}>";
    }
}
