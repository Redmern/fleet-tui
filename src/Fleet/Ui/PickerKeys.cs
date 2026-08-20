using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Ui;

public static class PickerKeys
{
    private const string Fallbacks = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static IReadOnlyList<FleetRow> Rows(
        IReadOnlyList<PickerEntry> entries, IReadOnlyList<string> keys)
    {
        var labelWidth = entries.Max(e => e.Label.Length);

        return
        [
            .. entries.Select((e, i) => new FleetRow(
                [
                    new FleetSpan(keys[i].Length == 0 ? "   " : $"{keys[i]}  ", FleetTones.Key),
                    FleetSpan.Plain(e.Label.PadRight(labelWidth)),
                ],
                e.Detail.Length == 0 ? null : [FleetSpan.Muted($"{e.Detail} ")])),
        ];
    }

    public static IReadOnlyList<string> For(
        IReadOnlyList<string> labels, IReadOnlySet<char>? reserved = null)
    {
        var taken = new HashSet<char>((IEnumerable<char>?)reserved ?? Array.Empty<char>());
        var keys = new List<string>(labels.Count);

        foreach (var label in labels)
        {
            keys.Add(Pick(label, taken));
        }

        return keys;
    }

    public static IReadOnlyList<string> For(
        IReadOnlyList<PickerEntry> entries, IReadOnlySet<char>? reserved = null)
    {
        var taken = new HashSet<char>((IEnumerable<char>?)reserved ?? Array.Empty<char>());
        var keys = new string[entries.Count];

        for (var i = 0; i < entries.Count; i++)
        {
            var wanted = entries[i].Key;

            if (wanted.Length == 1 && taken.Add(char.ToLowerInvariant(wanted[0])))
            {
                keys[i] = wanted.ToLowerInvariant();
            }
        }

        for (var i = 0; i < entries.Count; i++)
        {
            keys[i] ??= Pick(entries[i].Label, taken);
        }

        return keys;
    }

    public static IReadOnlyList<string> Label(
        IReadOnlyList<string> labels, IReadOnlyList<string> keys) =>
        [.. labels.Select((l, i) => keys[i].Length == 0 ? $"   {l}" : $"{keys[i]}  {l}")];

    private static string Pick(string label, HashSet<char> taken)
    {
        foreach (var c in label)
        {
            var lower = char.ToLowerInvariant(c);

            if (char.IsAsciiLetterOrDigit(lower) && taken.Add(lower))
            {
                return lower.ToString();
            }
        }

        foreach (var c in Fallbacks)
        {
            if (taken.Add(c))
            {
                return c.ToString();
            }
        }

        return string.Empty;
    }
}
