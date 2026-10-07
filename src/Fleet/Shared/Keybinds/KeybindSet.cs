using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keybinds.Models;

namespace Fleet.Shared.Keybinds;

public sealed class KeybindSet
{
    public KeybindSet(IEnumerable<KeybindEntry> entries)
    {
        Entries = [.. entries];
    }

    public static KeybindSet Empty { get; } = new([]);

    public IReadOnlyList<KeybindEntry> Entries { get; }

    public KeybindEntry? Find(string id) =>
        Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    public KeybindSet With(KeybindEntry entry)
    {
        var index = Entries.ToList().FindIndex(e => string.Equals(e.Id, entry.Id, StringComparison.Ordinal));

        if (index < 0)
        {
            return new KeybindSet([.. Entries, entry]);
        }

        var entries = Entries.ToList();
        entries[index] = entry;
        return new KeybindSet(entries);
    }

    public IReadOnlyList<KeybindBinding> For(KeybindTarget target, KeybindOs os) =>
        [.. Entries
            .Where(e => e.Targets.ContainsKey(target) && e.IsBoundOn(os))
            .Select(e => new KeybindBinding(e.Id, e.Action, e.ChordOn(os), e.Targets[target]))];

    public IReadOnlyList<KeybindBinding> For(KeybindTarget target) => For(target, KeybindNames.CurrentOs);
}
