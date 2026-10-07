using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Platform.Mux.Embedded.Input;

public static class MuxKeybinds
{
    public const string Leader = "leader";

    public const string SmartFocus = "smart-focus";

    public const string SmartResize = "smart-resize";

    private static readonly string[] Directions = ["left", "right", "up", "down"];

    public static string Prefix(KeybindSet set, KeybindOs os) =>
        set.For(KeybindTarget.Mux, os).LastOrDefault(b => b.Contexts.Contains(Leader)) is { } leader
            ? Spec(leader.Chord)
            : throw new InvalidOperationException("the keybind model has no mux prefix");

    public static IReadOnlyDictionary<string, string> Keys(KeybindSet set, KeybindOs os, string context)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var binding in set.For(KeybindTarget.Mux, os).Where(b => b.Contexts.Contains(context)))
        {
            if (Spec(binding.Chord) is var spec && Parses(spec))
            {
                keys[spec] = Command(binding.Action, context);
            }
        }

        return keys;
    }

    public static string Command(string action, string context)
    {
        var direct = string.Equals(context, KeybindLegacy.MuxDirect, StringComparison.Ordinal);

        return Directed(action, "focus-") is { } focus ? (direct ? $"{SmartFocus} {focus}" : action)
            : Directed(action, "resize-") is { } resize ? (direct ? $"{SmartResize} {resize}" : $"resize {resize}")
            : action;
    }

    public static string Spec(string chord) =>
        string.Join(' ', chord.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Step));

    private static string? Directed(string action, string verb) =>
        action.StartsWith(verb, StringComparison.Ordinal) && Directions.Contains(action[verb.Length..])
            ? action[verb.Length..]
            : null;

    private static bool Parses(string spec)
    {
        try
        {
            return spec.Split(' ').All(step => KeyChord.Parse(step) is not null);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Step(string step)
    {
        var cut = step.Length > 1 && step[^1] == '+' ? step.Length - 1 : step.LastIndexOf('+') + 1;
        var key = step[cut..];

        return step[..cut].ToLowerInvariant() + (key.Length == 1 && cut == 0 ? key : key.ToLowerInvariant());
    }
}
