using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fleet.Platform.Claude.Models;

public sealed class ClaudeKeybindingBlock
{
    [JsonPropertyName("context")]
    public string Context { get; set; } = string.Empty;

    [JsonPropertyName("bindings")]
    public Dictionary<string, string?> Bindings { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
