using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Input;

public static class WhichKey
{
    public static List<WhichKeyEntry> For(MuxKeys keys)
    {
        var entries = new List<WhichKeyEntry>();
        var bindings = keys.PrefixKeys.ToList();

        Group(bindings, entries, b => b.Command.StartsWith("focus-", StringComparison.Ordinal), "focus");
        Group(bindings, entries, b => b.Command.StartsWith("resize ", StringComparison.Ordinal), "resize");
        Group(bindings, entries, b => b.Command.StartsWith("tab ", StringComparison.Ordinal), "go to tab");

        foreach (var binding in bindings)
        {
            entries.Add(new WhichKeyEntry { Key = binding.Chord.Label, Label = Label(binding.Command) });
        }

        entries.Add(new WhichKeyEntry { Key = keys.Prefix.Label, Label = "send " + keys.Prefix.Label });
        return entries;
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

    private static void Group(
        List<MuxKeys.Binding> bindings, List<WhichKeyEntry> entries, Func<MuxKeys.Binding, bool> member, string label)
    {
        var members = bindings.Where(member).ToList();
        if (members.Count == 0)
        {
            return;
        }

        entries.Add(new WhichKeyEntry { Key = string.Join(' ', members.Select(m => m.Chord.Label)), Label = label });
        bindings.RemoveAll(b => members.Contains(b));
    }
}