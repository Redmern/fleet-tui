namespace Fleet.Shared.Keybinds;

public static class KeybindChord
{
    public const string Unbound = "none";

    private static readonly string[] ModifierOrder = ["Ctrl", "Alt", "Shift", "Super"];

    private static readonly IReadOnlyDictionary<string, string> Modifiers =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = "Ctrl",
            ["control"] = "Ctrl",
            ["alt"] = "Alt",
            ["meta"] = "Alt",
            ["option"] = "Alt",
            ["opt"] = "Alt",
            ["shift"] = "Shift",
            ["super"] = "Super",
            ["cmd"] = "Super",
            ["win"] = "Super",
        };

    private static readonly IReadOnlyDictionary<string, string> NamedKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["enter"] = "Enter",
            ["return"] = "Enter",
            ["space"] = "Space",
            ["tab"] = "Tab",
            ["esc"] = "Escape",
            ["escape"] = "Escape",
            ["backspace"] = "Backspace",
            ["delete"] = "Delete",
            ["del"] = "Delete",
            ["insert"] = "Insert",
            ["home"] = "Home",
            ["end"] = "End",
            ["pageup"] = "PageUp",
            ["pagedown"] = "PageDown",
            ["left"] = "Left",
            ["right"] = "Right",
            ["up"] = "Up",
            ["down"] = "Down",
        };

    public static bool IsUnbound(string? chord) =>
        string.IsNullOrWhiteSpace(chord)
        || string.Equals(chord.Trim(), Unbound, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string chord)
    {
        if (IsUnbound(chord))
        {
            return Unbound;
        }

        return string.Join(' ', chord.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Step));
    }

    public static bool TryNormalize(string chord, out string normalized)
    {
        try
        {
            normalized = Normalize(chord);
            return true;
        }
        catch (FormatException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    public static bool SameAs(string left, string right) =>
        TryNormalize(left, out var l)
        && TryNormalize(right, out var r)
        && string.Equals(l, r, StringComparison.Ordinal);

    private static string Step(string step)
    {
        var parts = Split(step);
        var modifiers = parts[..^1]
            .Select(m => Modifiers.TryGetValue(m, out var name)
                ? name
                : throw new FormatException($"unknown modifier '{m}' in '{step}'"))
            .Distinct()
            .OrderBy(m => Array.IndexOf(ModifierOrder, m));

        return string.Join('+', [.. modifiers, Key(parts[^1])]);
    }

    private static string[] Split(string step)
    {
        if (step == "+")
        {
            return ["+"];
        }

        if (step.EndsWith("++", StringComparison.Ordinal))
        {
            return [.. Split(step[..^2] + "+x")[..^1], "+"];
        }

        var parts = step.Split('+');

        if (parts.Any(p => p.Length == 0))
        {
            throw new FormatException($"empty key in '{step}'");
        }

        return parts;
    }

    private static string Key(string key)
    {
        if (key.Length == 1)
        {
            return key;
        }

        if (NamedKeys.TryGetValue(key, out var named))
        {
            return named;
        }

        if (key[0] is 'f' or 'F' && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24)
        {
            return $"F{number}";
        }

        return char.ToUpperInvariant(key[0]) + key[1..];
    }
}
