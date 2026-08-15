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

public sealed class ClaudeSettingsFile
{
    [JsonPropertyName("permissions")]
    public PermissionsJson Permissions { get; set; } = new();

    [JsonPropertyName("enabledMcpjsonServers")]
    public List<string> EnabledMcpjsonServers { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}
