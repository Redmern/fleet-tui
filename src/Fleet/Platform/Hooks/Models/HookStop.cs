using System.Text.Json.Serialization;

namespace Fleet.Platform.Hooks.Models;

public sealed class HookStop
{
    [JsonPropertyName("continue")]
    public bool Continue { get; set; }

    [JsonPropertyName("stopReason")]
    public string StopReason { get; set; } = string.Empty;
}
