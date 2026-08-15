using System.Text.Json.Serialization;

namespace Fleet.Platform.Mcp.Models;

public sealed class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public sealed class ToolsCapability;

public sealed class Capabilities
{
    [JsonPropertyName("tools")]
    public ToolsCapability Tools { get; set; } = new();
}

public sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public Capabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public ServerInfo ServerInfo { get; set; } = new();
}

public sealed class SchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public sealed class SchemaObject
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, SchemaProperty> Properties { get; set; } = [];

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = [];
}

public sealed class ToolListItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public sealed class TextBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public sealed class CallResult
{
    [JsonPropertyName("content")]
    public List<TextBlock> Content { get; set; } = [];

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

public sealed class RpcErrorBody
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
