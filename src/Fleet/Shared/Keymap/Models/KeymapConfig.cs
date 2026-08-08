using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap.Models;

public sealed record KeymapConfig(string Prefix, IReadOnlyDictionary<FleetAction, string> Bindings)
{
    public static KeymapConfig Default => new(
        KeymapDefaults.Prefix,
        KeymapDefaults.Bindings.ToDictionary(b => b.Key, b => b.Value));

    public KeymapConfig With(FleetAction action, string key)
    {
        var bindings = Bindings.ToDictionary(b => b.Key, b => b.Value);
        bindings[action] = key;
        return new KeymapConfig(Prefix, bindings);
    }

    public KeymapConfig WithPrefix(string prefix) => new(prefix, Bindings);

    public KeymapConfig MergedOverDefaults()
    {
        var bindings = KeymapDefaults.Bindings.ToDictionary(b => b.Key, b => b.Value);

        foreach (var (action, key) in Bindings)
        {
            if (!string.IsNullOrWhiteSpace(key) && bindings.ContainsKey(action))
            {
                bindings[action] = key;
            }
        }

        return new KeymapConfig(
            string.IsNullOrWhiteSpace(Prefix) ? KeymapDefaults.Prefix : Prefix,
            bindings);
    }
}
