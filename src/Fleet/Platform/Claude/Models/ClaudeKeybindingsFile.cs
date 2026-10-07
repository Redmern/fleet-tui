using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fleet.Platform.Claude.Models;

public sealed class ClaudeKeybindingsFile
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("$docs")]
    public string? Docs { get; set; }

    [JsonPropertyName("bindings")]
    public List<ClaudeKeybindingBlock> Bindings { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
