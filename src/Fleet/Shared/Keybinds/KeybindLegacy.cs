using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keybinds.Models;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keybinds;

public static class KeybindLegacy
{
    public const string FleetUiPrefixId = "fleet-ui.prefix";

    public const string MuxPrefixId = "mux.prefix";

    public const string MuxDirect = "direct";

    public const string MuxPrefixed = "prefix";

    public static string FleetUiId(FleetAction action) => "fleet-ui." + KeybindNames.Kebab(action.ToString());

    public static KeybindSet FleetUi(
        KeybindSet set, string? prefix, IReadOnlyDictionary<string, string>? bindings)
    {
        set = Rechord(set, FleetUiPrefixId, prefix);

        foreach (var (name, key) in bindings ?? new Dictionary<string, string>())
        {
            if (Enum.TryParse<FleetAction>(name, ignoreCase: true, out var action) && action != FleetAction.None)
            {
                set = Rechord(set, FleetUiId(action), key);
            }
        }

        return set;
    }

    public static KeybindSet Mux(
        KeybindSet set,
        string? prefix,
        IReadOnlyDictionary<string, string>? prefixKeys,
        IReadOnlyDictionary<string, string>? keys,
        Action<string>? log = null)
    {
        log ??= _ => { };
        set = Rechord(set, MuxPrefixId, prefix);

        foreach (var (spec, command) in keys ?? new Dictionary<string, string>())
        {
            if (spec.Trim().Contains(' '))
            {
                continue;
            }

            set = Remap(set, spec, command, MuxDirect, log);
        }

        foreach (var (spec, command) in prefixKeys ?? new Dictionary<string, string>())
        {
            set = Remap(set, spec, command, MuxPrefixed, log);
        }

        return set;
    }

    private static KeybindSet Rechord(KeybindSet set, string id, string? chord)
    {
        if (string.IsNullOrWhiteSpace(chord)
            || set.Find(id) is not { } entry
            || !KeybindChord.TryNormalize(chord, out var normalized))
        {
            return set;
        }

        return set.With(entry with { Chord = normalized });
    }

    private static KeybindSet Remap(KeybindSet set, string spec, string? command, string context, Action<string> log)
    {
        if (!TryMuxChord(spec, out var chord))
        {
            log($"keybinds: mux key \"{spec}\" not migrated, it is not a chord");
            return set;
        }

        var unbound = KeybindChord.IsUnbound(command);
        var action = unbound ? null : KeybindActions.FromMux(command!);
        var matches = set.Entries
            .Where(e => e.Targets.TryGetValue(KeybindTarget.Mux, out var contexts) && contexts.Contains(context))
            .Where(e => KeybindChord.SameAs(e.Chord, chord))
            .ToList();
        var split = false;

        foreach (var match in matches)
        {
            if (string.Equals(match.Action, action, StringComparison.Ordinal))
            {
                continue;
            }

            if (match.Targets.Count == 1 && match.Targets[KeybindTarget.Mux].Count == 1)
            {
                set = set.With(unbound
                    ? match with { Chord = KeybindChord.Unbound, OsChords = new Dictionary<KeybindOs, string>() }
                    : match with { Action = action! });
                continue;
            }

            set = set.With(match with { Targets = Without(match.Targets, KeybindTarget.Mux, context) });
            split = true;
        }

        if (action is not null && (split || matches.Count == 0))
        {
            set = set.With(new KeybindEntry(
                $"mux.{context}.{chord}",
                action,
                chord,
                new Dictionary<KeybindTarget, IReadOnlyList<string>> { [KeybindTarget.Mux] = [context] },
                new Dictionary<KeybindOs, string>()));
        }

        return set;
    }

    private static bool TryMuxChord(string spec, out string chord)
    {
        if (!KeybindChord.TryNormalize(spec == " " ? "Space" : spec, out chord) || KeybindChord.IsUnbound(chord))
        {
            return false;
        }

        chord = string.Join(' ', chord.Split(' ').Select(LowerModifiedLetter));
        return true;
    }

    private static string LowerModifiedLetter(string step)
    {
        var plus = step.LastIndexOf('+');

        return plus > 0 && plus == step.Length - 2 && char.IsAsciiLetter(step[^1])
            ? step[..^1] + char.ToLowerInvariant(step[^1])
            : step;
    }

    private static Dictionary<KeybindTarget, IReadOnlyList<string>> Without(
        IReadOnlyDictionary<KeybindTarget, IReadOnlyList<string>> targets, KeybindTarget target, string context)
    {
        var left = targets.ToDictionary(t => t.Key, t => t.Value);
        var contexts = left[target].Where(c => !string.Equals(c, context, StringComparison.Ordinal)).ToList();

        if (contexts.Count == 0)
        {
            left.Remove(target);
        }
        else
        {
            left[target] = contexts;
        }

        return left;
    }
}
