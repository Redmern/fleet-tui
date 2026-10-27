using Fleet.Shared.Keymap.Enums;

namespace Fleet.Ui;

public static class FleetMnemonics
{
    private const string Fallbacks = "abcdefghijklmnopqrstuvwxyz";

    private static readonly FleetAction[] Motions =
    [
        FleetAction.MoveDown,
        FleetAction.MoveUp,
        FleetAction.MoveFirst,
        FleetAction.MoveLast,
        FleetAction.PageDown,
        FleetAction.PageUp,
    ];

    public static IReadOnlyList<string> For(
        IReadOnlyList<string> labels, Keymap keymap, IEnumerable<string>? taken = null)
    {
        var used = new HashSet<char>();

        foreach (var key in (taken ?? []).Concat(Motions.Select(keymap.TextFor)))
        {
            if (key.Length == 1)
            {
                used.Add(key[0]);
            }
        }

        return [.. labels.Select(label => Pick(label, used))];
    }

    private static string Pick(string label, HashSet<char> used)
    {
        foreach (var c in label.ToLowerInvariant().Concat(Fallbacks))
        {
            if (char.IsAsciiLetterLower(c) && used.Add(c))
            {
                return c.ToString();
            }
        }

        return string.Empty;
    }
}
