using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Terminal.Gui.Input;

namespace Fleet.Ui;

public sealed class Keymap
{
    private readonly Dictionary<FleetAction, Key> _keys = [];

    public Keymap(KeymapConfig config)
    {
        Config = config.MergedOverDefaults();
        Prefix = Parse(Config.Prefix, KeymapDefaults.Prefix);

        foreach (var (action, text) in Config.Bindings)
        {
            _keys[action] = Parse(text, KeymapDefaults.Bindings[action]);
        }
    }

    public KeymapConfig Config { get; }

    public Key Prefix { get; }

    public static Keymap Default => new(KeymapConfig.Default);

    public Key KeyFor(FleetAction action) =>
        _keys.TryGetValue(action, out var key) ? key : Key.Empty;

    public string TextFor(FleetAction action) =>
        Config.Bindings.TryGetValue(action, out var text) ? text : string.Empty;

    public string PrefixText => Config.Prefix;

    public FleetAction ActionFor(Key key)
    {
        foreach (var (action, bound) in _keys)
        {
            if (bound == key)
            {
                return action;
            }
        }

        return FleetAction.None;
    }

    private static Key Parse(string text, string fallback)
    {
        try
        {
            var key = new Key(text);
            return key.IsValid ? key : new Key(fallback);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return new Key(fallback);
        }
    }
}
