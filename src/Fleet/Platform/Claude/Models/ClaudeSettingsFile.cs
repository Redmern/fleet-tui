using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fleet.Platform.Claude.Models;

public sealed class PermissionsJson
{
    [JsonPropertyName("allow")]
    public List<string> Allow { get; set; } = [];

    [JsonPropertyName("deny")]
    public List<string> Deny { get; set; } = [];

    [JsonPropertyName("ask")]
    public List<string> Ask { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}

public sealed class HookEntry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "command";

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("timeout")]
    public double? Timeout { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}

public sealed class HookGroup
{
    [JsonPropertyName("matcher")]
    public string? Matcher { get; set; }

    [JsonPropertyName("hooks")]
    public List<HookEntry> Hooks { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}

public sealed class HooksJson
{
    [JsonPropertyName("UserPromptSubmit")]
    public List<HookGroup> UserPromptSubmit { get; set; } = [];

    [JsonPropertyName("PreToolUse")]
    public List<HookGroup>? PreToolUse { get; set; }

    [JsonPropertyName("PostToolUse")]
    public List<HookGroup>? PostToolUse { get; set; }

    [JsonPropertyName("Stop")]
    public List<HookGroup>? Stop { get; set; }

    [JsonPropertyName("SessionStart")]
    public List<HookGroup>? SessionStart { get; set; }

    [JsonPropertyName("SessionEnd")]
    public List<HookGroup>? SessionEnd { get; set; }

    [JsonPropertyName("PermissionRequest")]
    public List<HookGroup>? PermissionRequest { get; set; }

    [JsonPropertyName("Notification")]
    public List<HookGroup>? Notification { get; set; }

    public List<HookGroup>? For(string name) => name switch
    {
        "UserPromptSubmit" => UserPromptSubmit,
        "PreToolUse" => PreToolUse,
        "PostToolUse" => PostToolUse,
        "Stop" => Stop,
        "SessionStart" => SessionStart,
        "SessionEnd" => SessionEnd,
        "PermissionRequest" => PermissionRequest,
        "Notification" => Notification,
        _ => null,
    };

    public void Set(string name, List<HookGroup>? groups)
    {
        switch (name)
        {
            case "UserPromptSubmit":
                UserPromptSubmit = groups ?? [];
                break;
            case "PreToolUse":
                PreToolUse = groups;
                break;
            case "PostToolUse":
                PostToolUse = groups;
                break;
            case "Stop":
                Stop = groups;
                break;
            case "SessionStart":
                SessionStart = groups;
                break;
            case "SessionEnd":
                SessionEnd = groups;
                break;
            case "PermissionRequest":
                PermissionRequest = groups;
                break;
            case "Notification":
                Notification = groups;
                break;
        }
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}

public sealed class UserSettingsFile
{
    [JsonPropertyName("enabledMcpjsonServers")]
    public List<string> EnabledMcpjsonServers { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}

public sealed class ClaudeSettingsFile
{
    [JsonPropertyName("permissions")]
    public PermissionsJson Permissions { get; set; } = new();

    [JsonPropertyName("enabledMcpjsonServers")]
    public List<string> EnabledMcpjsonServers { get; set; } = [];

    [JsonPropertyName("enableAllProjectMcpServers")]
    public bool? EnableAllProjectMcpServers { get; set; }

    [JsonPropertyName("hooks")]
    public HooksJson? Hooks { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}
