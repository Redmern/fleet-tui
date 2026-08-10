using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap;

public static class KeymapDiff
{
    public static IReadOnlyDictionary<FleetAction, string> AgainstDefaults(
        IReadOnlyDictionary<FleetAction, string> bindings)
    {
        var changed = new Dictionary<FleetAction, string>();

        foreach (var (action, key) in bindings)
        {
            if (!KeymapDefaults.Bindings.TryGetValue(action, out var shipped))
            {
                continue;
            }

            if (!string.Equals(shipped, key, StringComparison.Ordinal))
            {
                changed[action] = key;
            }
        }

        return changed;
    }
}
