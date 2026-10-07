using System.Text.Json;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keybinds.Models;

namespace Fleet.Shared.Keybinds;

public static class KeybindLayering
{
    public static KeybindsFile Parse(string json) =>
        JsonSerializer.Deserialize(json, KeybindsJsonContext.Default.KeybindsFile)
        ?? throw new JsonException("the keybinds file is empty");

    public static KeybindSet Apply(
        KeybindSet set, IReadOnlyDictionary<string, KeybindEntryJson>? overrides, Action<string>? log = null)
    {
        log ??= _ => { };

        foreach (var (id, layer) in overrides ?? new Dictionary<string, KeybindEntryJson>())
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                log("keybinds: an entry without an id ignored");
                continue;
            }

            if (Merge(set.Find(id.Trim()), id.Trim(), layer, log) is { } entry)
            {
                set = set.With(entry);
            }
        }

        return set;
    }

    private static KeybindEntry? Merge(KeybindEntry? existing, string id, KeybindEntryJson? layer, Action<string> log)
    {
        if (layer is null)
        {
            return null;
        }

        string? chord = existing?.Chord;
        if (layer.Chord is not null)
        {
            if (!KeybindChord.TryNormalize(layer.Chord, out var normalized))
            {
                log($"keybinds: \"{id}\" ignored, \"{layer.Chord}\" is not a chord");
                return null;
            }

            chord = normalized;
        }

        var action = string.IsNullOrWhiteSpace(layer.Action) ? existing?.Action : layer.Action.Trim();
        var targets = layer.Targets is null ? existing?.Targets : Targets(id, layer.Targets, log);

        if (action is null || chord is null || targets is null || (existing is null && targets.Count == 0))
        {
            log($"keybinds: \"{id}\" ignored, a new keybind needs an action, a chord and targets");
            return null;
        }

        var unbindsAll = layer.Chord is not null && KeybindChord.IsUnbound(chord) && layer.Os is null;
        var os = unbindsAll ? new Dictionary<KeybindOs, string>() : OsChords(id, existing?.OsChords, layer.Os, log);

        return new KeybindEntry(id, action, chord, targets, os);
    }

    private static Dictionary<KeybindTarget, IReadOnlyList<string>> Targets(
        string id, Dictionary<string, List<string>> targets, Action<string> log)
    {
        var parsed = new Dictionary<KeybindTarget, IReadOnlyList<string>>();

        foreach (var (name, contexts) in targets)
        {
            if (!KeybindNames.TryTarget(name, out var target))
            {
                log($"keybinds: \"{id}\": target \"{name}\" ignored, targets are fleet-ui, mux, nvim and claude");
                continue;
            }

            parsed[target] = [.. (contexts ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())];
        }

        return parsed;
    }

    private static Dictionary<KeybindOs, string> OsChords(
        string id,
        IReadOnlyDictionary<KeybindOs, string>? existing,
        Dictionary<string, string>? layer,
        Action<string> log)
    {
        var chords = existing?.ToDictionary(c => c.Key, c => c.Value) ?? [];

        foreach (var (name, chord) in layer ?? [])
        {
            if (!KeybindNames.TryOs(name, out var os))
            {
                log($"keybinds: \"{id}\": os \"{name}\" ignored, systems are windows, linux and macos");
                continue;
            }

            if (!KeybindChord.TryNormalize(chord ?? string.Empty, out var normalized))
            {
                log($"keybinds: \"{id}\": {name} chord \"{chord}\" ignored, it is not a chord");
                continue;
            }

            chords[os] = normalized;
        }

        return chords;
    }
}
