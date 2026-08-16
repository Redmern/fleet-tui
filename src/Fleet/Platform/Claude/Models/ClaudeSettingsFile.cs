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
