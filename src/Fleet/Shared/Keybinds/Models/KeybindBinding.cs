namespace Fleet.Shared.Keybinds.Models;

public sealed record KeybindBinding(string Id, string Action, string Chord, IReadOnlyList<string> Contexts);
