using System.Text.Json.Serialization;

namespace Fleet.Platform.Claude.Models;

public sealed record ClaudeKeybinding(
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("action")] string Action);
