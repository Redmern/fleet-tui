using Fleet.Platform.Claude.Models;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Platform.Claude;

public static class ClaudeKeybindings
{
    public const string FileName = "keybindings.json";

    public const string OwnershipFileName = "keybindings.fleet.json";

    public const string Schema = "https://www.schemastore.org/claude-code-keybindings.json";

    public const string Docs = "https://code.claude.com/docs/en/keybindings";

    private static readonly HashSet<string> Contexts = new(StringComparer.Ordinal)
    {
        "Global", "Chat", "Autocomplete", "Settings", "Confirmation", "Tabs", "Help", "Transcript",
        "HistorySearch", "Task", "ThemePicker", "Attachments", "Footer", "MessageSelector", "DiffDialog",
        "DiffPanel", "ModelPicker", "EffortSlider", "Select", "Plugin", "Pane", "PaneField", "Agents", "Scroll",
    };

    private static readonly IReadOnlyDictionary<string, string> Actions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["newline"] = "chat:newline",
        };

    private static readonly IReadOnlyDictionary<string, string> Modifiers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Ctrl"] = "ctrl",
            ["Alt"] = "alt",
            ["Shift"] = "shift",
            ["Super"] = "cmd",
        };

    private static readonly IReadOnlyDictionary<string, string> NamedKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Enter"] = "enter",
            ["Space"] = "space",
            ["Tab"] = "tab",
            ["Escape"] = "escape",
            ["Backspace"] = "backspace",
            ["Delete"] = "delete",
            ["Home"] = "home",
            ["End"] = "end",
            ["PageUp"] = "pageup",
            ["PageDown"] = "pagedown",
            ["Left"] = "left",
            ["Right"] = "right",
            ["Up"] = "up",
            ["Down"] = "down",
        };

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "ctrl+c", "ctrl+d", "ctrl+m", "ctrl+[", "ctrl+i", "ctrl+h",
    };

    public static string PathIn(string configDirectory) => Path.Combine(configDirectory, FileName);

    public static string OwnershipPathIn(string configDirectory) =>
        Path.Combine(configDirectory, OwnershipFileName);

    public static IReadOnlyList<ClaudeKeybinding> Render(KeybindSet set, KeybindOs os, Action<string>? log = null)
    {
        log ??= _ => { };
        var rendered = new List<ClaudeKeybinding>();

        foreach (var binding in set.For(KeybindTarget.Claude, os))
        {
            if (Action(binding.Action) is not { } action)
            {
                log($"keybinds: \"{binding.Id}\" not written to Claude, it has no Claude action for \"{binding.Action}\"");
                continue;
            }

            if (Key(binding.Chord) is not { } key)
            {
                log($"keybinds: \"{binding.Id}\" not written to Claude, \"{binding.Chord}\" is not a Claude keystroke");
                continue;
            }

            if (key.Split(' ').Any(Reserved.Contains))
            {
                log($"keybinds: \"{binding.Id}\" not written to Claude, \"{key}\" is reserved by Claude Code");
                continue;
            }

            if (binding.Contexts.Count == 0)
            {
                log($"keybinds: \"{binding.Id}\" not written to Claude, it names no Claude context");
                continue;
            }

            foreach (var context in binding.Contexts)
            {
                if (!Contexts.Contains(context))
                {
                    log($"keybinds: \"{binding.Id}\": Claude context \"{context}\" ignored, it is not a Claude Code context");
                    continue;
                }

                if (rendered.Any(r => r.Context == context && r.Key == key))
                {
                    log($"keybinds: \"{binding.Id}\": \"{key}\" in {context} ignored, another keybind already takes it");
                    continue;
                }

                rendered.Add(new ClaudeKeybinding(context, key, action));
            }
        }

        return rendered;
    }

    public static IReadOnlyList<ClaudeKeybinding> Render(KeybindSet set, Action<string>? log = null) =>
        Render(set, KeybindNames.CurrentOs, log);

    public static bool SameKey(string left, string right) =>
        KeybindChord.TryNormalize(left, out var l) && KeybindChord.TryNormalize(right, out var r)
            ? Folded(l) == Folded(r)
            : string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Folded(string normalized) =>
        string.Join(' ', normalized.Split(' ').Select(step => step.ToLowerInvariant()));

    private static string? Action(string action)
    {
        if (Actions.TryGetValue(action, out var mapped))
        {
            return mapped;
        }

        var colon = action.IndexOf(':');

        return colon > 0 && colon < action.Length - 1 && !action.Contains(' ') ? action : null;
    }

    private static string? Key(string chord)
    {
        if (!KeybindChord.TryNormalize(chord, out var normalized) || KeybindChord.IsUnbound(normalized))
        {
            return null;
        }

        var steps = normalized.Split(' ').Select(Step).ToList();

        return steps.Any(s => s is null) ? null : string.Join(' ', steps);
    }

    private static string? Step(string step)
    {
        var plus = step.Length > 1 ? step.LastIndexOf('+', step.Length - 2) : -1;
        var modifiers = plus < 0 ? [] : step[..plus].Split('+').Select(m => Modifiers[m]).ToList();
        var key = plus < 0 ? step : step[(plus + 1)..];

        string name;

        if (NamedKeys.TryGetValue(key, out var named))
        {
            name = named;
        }
        else if (key.Length == 1 && char.IsAsciiLetterUpper(key[0]))
        {
            if (!modifiers.Contains("shift"))
            {
                modifiers.Add("shift");
            }

            name = key.ToLowerInvariant();
        }
        else if (key.Length == 1 && key[0] is > ' ' and < (char)127 and not '+')
        {
            name = key;
        }
        else
        {
            return null;
        }

        return string.Join('+', [.. modifiers.OrderBy(Rank), name]);
    }

    private static int Rank(string modifier) => modifier switch
    {
        "ctrl" => 0,
        "alt" => 1,
        "shift" => 2,
        _ => 3,
    };
}
