using System.Text.Json.Serialization;

namespace Fleet.Platform.Claude.Models;

public sealed class ClaudeKeybindingsOwnership
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("owned")]
    public List<ClaudeKeybinding> Owned { get; set; } = [];
}
