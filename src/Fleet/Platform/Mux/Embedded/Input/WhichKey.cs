using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Input;

public static class WhichKey
{
    public static List<WhichKeyEntry> For(MuxKeys keys) => For(keys.Root, keys.Prefix);

    public static List<WhichKeyEntry> For(KeyNode node, KeyChord prefix)
    {
        var entries = new List<WhichKeyEntry>();
        var bindings = node.Leaves.ToList();

        Fold(bindings, entries, b => b.Command.StartsWith("focus-", StringComparison.Ordinal), "focus");
        Fold(bindings, entries, b => b.Command.StartsWith("resize ", StringComparison.Ordinal), "resize");
        Fold(bindings, entries, b => b.Command.StartsWith("tab ", StringComparison.Ordinal), "go to tab");

        foreach (var binding in bindings)
        {
            entries.Add(new WhichKeyEntry { Key = binding.Chord.Label, Label = Label(binding.Command) });
        }

        foreach (var group in node.Groups)
        {
            entries.Add(new WhichKeyEntry { Key = group.Chord.Label, Label = group.Node.Label, Group = true });
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
        _ => command.Replace('-', ' '),
    };

    private static void Fold(
        List<MuxKeys.Binding> bindings, List<WhichKeyEntry> entries, Func<MuxKeys.Binding, bool> member, string label)
    {
        var members = bindings.Where(member).ToList();
        if (members.Count == 0)
        {
            return;
        }

        entries.Add(new WhichKeyEntry { Key = Keys(members.Select(m => m.Chord.Label).ToList()), Label = label, Fold = true });
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