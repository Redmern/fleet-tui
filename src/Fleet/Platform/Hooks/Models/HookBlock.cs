using System.Text.Json.Serialization;

namespace Fleet.Platform.Hooks.Models;

public sealed class HookBlock
{
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = "block";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}
