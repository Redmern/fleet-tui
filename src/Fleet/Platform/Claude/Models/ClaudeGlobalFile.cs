using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fleet.Platform.Claude.Models;

public sealed class ClaudeProjectEntry
{
    [JsonPropertyName("hasTrustDialogAccepted")]
    public bool? HasTrustDialogAccepted { get; set; }

    [JsonPropertyName("enabledMcpjsonServers")]
    public List<string>? EnabledMcpjsonServers { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}

public sealed class ClaudeGlobalFile
{
    [JsonPropertyName("projects")]
    public Dictionary<string, ClaudeProjectEntry> Projects { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}
