using System.Text.Json.Serialization;

namespace Fleet.Platform.Claude.Models;

public sealed class ClaudeThemeFile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("base")]
    public string Base { get; set; } = "dark";

    [JsonPropertyName("overrides")]
    public Dictionary<string, string> Overrides { get; set; } = [];
}
