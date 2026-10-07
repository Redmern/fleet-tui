using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Shared.Keybinds.Models;

public sealed record KeybindEntry(
    string Id,
    string Action,
    string Chord,
    IReadOnlyDictionary<KeybindTarget, IReadOnlyList<string>> Targets,
    IReadOnlyDictionary<KeybindOs, string> OsChords)
{
    public string ChordOn(KeybindOs os) => OsChords.TryGetValue(os, out var chord) ? chord : Chord;

    public bool IsBoundOn(KeybindOs os) => !KeybindChord.IsUnbound(ChordOn(os));
}
