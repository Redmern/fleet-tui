using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;

namespace Fleet.Shared.Keymap;

public static class MenuKeys
{
    private const string Fallbacks = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static IReadOnlyList<MenuEntry> Assign(
        IReadOnlyList<FleetAction> actions, IReadOnlyDictionary<FleetAction, string> preferred)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<MenuEntry>(actions.Count);

        foreach (var action in actions)
        {
            var label = KeymapDefaults.Short(action);
            var key = Pick(action, label, preferred, taken);

            if (key is null)
            {
                continue;
            }

            taken.Add(key);
            entries.Add(new MenuEntry(action, key, label));
        }

        return entries;
    }

    private static string? Pick(
        FleetAction action,
        string label,
        IReadOnlyDictionary<FleetAction, string> preferred,
        HashSet<string> taken)
    {
        if (preferred.TryGetValue(action, out var wanted)
            && wanted.Length == 1
            && char.IsAsciiLetterOrDigit(wanted[0])
            && !taken.Contains(wanted))
        {
            return wanted;
        }

        foreach (var c in label.ToLowerInvariant())
        {
            var candidate = c.ToString();

            if (char.IsAsciiLetterOrDigit(c) && !taken.Contains(candidate))
            {
                return candidate;
            }
        }

        foreach (var c in Fallbacks)
        {
            var candidate = c.ToString();

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
