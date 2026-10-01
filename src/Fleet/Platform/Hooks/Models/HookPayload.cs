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

    [JsonPropertyName("hook_event_name")]
    public string? HookEventName { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }

    [JsonPropertyName("agent_id")]
    public string? AgentId { get; set; }

    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    public string Text => Prompt ?? UserPrompt ?? string.Empty;
}
