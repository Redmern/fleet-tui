using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Platform.Mux.Embedded.Input;

public static class WhichKey
{
    public const string FocusIcon = "";
    public const string ResizeIcon = "";
    public const string TabIcon = "";

    public static List<WhichKeyEntry> For(MuxKeys keys) => For(keys.Root, keys.Prefix, keys.ShowIcons);

    public static List<WhichKeyEntry> For(KeyNode node, KeyChord prefix, bool showIcons = true)
    {
        var entries = new List<WhichKeyEntry>();
        var bindings = node.Leaves.ToList();

        Fold(bindings, entries, b => b.Command.StartsWith("focus-", StringComparison.Ordinal), "focus", showIcons ? FocusIcon : null);
        Fold(bindings, entries, b => b.Command.StartsWith("resize ", StringComparison.Ordinal), "resize", showIcons ? ResizeIcon : null);
        Fold(bindings, entries, b => b.Command.StartsWith("tab ", StringComparison.Ordinal), "go to tab", showIcons ? TabIcon : null);

        foreach (var binding in bindings)
        {
            entries.Add(new WhichKeyEntry { Key = binding.Chord.Label, Label = Label(binding.Command) });
        }

        foreach (var group in node.Groups)
        {
            entries.Add(new WhichKeyEntry
            {
                Key = group.Chord.Label,
                Label = group.Node.Label,
                Group = true,
                Icon = showIcons ? group.Node.Icon : null,
            });
        }

        if (node.Parent is null)
        {
            entries.Add(new WhichKeyEntry { Key = prefix.Label, Label = "send " + prefix.Label });
        }

        return entries
            .OrderBy(e => !(e.Group || e.Fold))
            .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
    }

    public static string Label(string command) => command switch
    {
        "split-right" => "split right",
        "split-down" => "split down",
        "kill-pane" => "close pane",
        "kill-tab" => "close tab",
        "switch-project" => "switch project",
        "next-workspace" => "next project",
        "menu" => "fleet menu",
        "float-new" => "new float",
        "float-toggle" => "show/hide floats",
        "float-embed" => "float ↔ tile",
        "float-mode" => "move/resize float",
        "reload" => "reload keys",
        _ when MenuAction(command) is { } action => KeymapDefaults.Describe(action).ToLowerInvariant(),
        _ => command.Replace('-', ' '),
    };

    private static FleetAction? MenuAction(string command) =>
        command.StartsWith("menu ", StringComparison.Ordinal)
        && FleetActionIds.Parse(command["menu ".Length..]) is var action and not FleetAction.None
            ? action
            : null;

    private static void Fold(
        List<MuxKeys.Binding> bindings,
        List<WhichKeyEntry> entries,
        Func<MuxKeys.Binding, bool> member,
        string label,
        string? icon)
    {
        var members = bindings.Where(member).ToList();
        if (members.Count == 0)
        {
            return;
        }

        entries.Add(new WhichKeyEntry
        {
            Key = Keys(members.Select(m => m.Chord.Label).ToList()),
            Label = label,
            Fold = true,
            Icon = icon,
        });
        bindings.RemoveAll(b => members.Contains(b));
    }

    public static string Keys(IReadOnlyList<string> labels)
    {
        var digitRun = labels.Count >= 3
            && labels.All(l => l.Length == 1 && char.IsAsciiDigit(l[0]))
            && labels.Zip(labels.Skip(1)).All(p => p.Second[0] == p.First[0] + 1);

        return digitRun ? $"{labels[0]}-{labels[^1]}" : string.Join(' ', labels);
    }
}