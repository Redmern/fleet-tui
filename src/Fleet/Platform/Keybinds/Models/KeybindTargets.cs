namespace Fleet.Platform.Keybinds.Models;

public sealed record KeybindTargets(string NvimDirectory, string UserModule, IEnumerable<string> ClaudeHomes);
