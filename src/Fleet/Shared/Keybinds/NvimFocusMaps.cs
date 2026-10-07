using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Shared.Keybinds;

public static class NvimFocusMaps
{
    private static readonly IReadOnlyDictionary<string, (string Wincmd, string Direction)> Directions =
        new Dictionary<string, (string, string)>
        {
            ["focus-left"] = ("h", "Left"),
            ["focus-down"] = ("j", "Down"),
            ["focus-up"] = ("k", "Up"),
            ["focus-right"] = ("l", "Right"),
        };

    public static string LuaTable(KeybindSet set) =>
        "{" + string.Join(',', set.For(KeybindTarget.Nvim)
            .Where(b => Directions.ContainsKey(b.Action) && b.Contexts.Count > 0)
            .Select(b => Row(NvimChord.Lhs(b.Chord), Directions[b.Action], b.Contexts))) + "}";

    private static string Row(string lhs, (string Wincmd, string Direction) to, IReadOnlyList<string> modes) =>
        $"{{{Quote(lhs)},'{to.Wincmd}','{to.Direction}',{{{string.Join(',', modes.Select(Quote))}}}}}";

    private static string Quote(string text) =>
        "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";
}
