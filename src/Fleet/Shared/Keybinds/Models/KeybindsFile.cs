using System.Text.Json.Serialization;

namespace Fleet.Shared.Keybinds.Models;

public sealed class KeybindsFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("keybinds")]
    public Dictionary<string, KeybindEntryJson> Keybinds { get; set; } = [];
}
