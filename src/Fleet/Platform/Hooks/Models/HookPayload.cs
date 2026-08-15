using System.Text.Json.Serialization;

namespace Fleet.Platform.Hooks.Models;

public sealed class HookPayload
{
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("user_prompt")]
    public string? UserPrompt { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    public string Text => Prompt ?? UserPrompt ?? string.Empty;
}
