using System.Text.Json.Serialization;
using Fleet.Platform.Mcp.Models;

namespace Fleet.Platform.Mcp;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(SchemaObject))]
[JsonSerializable(typeof(ToolListItem))]
[JsonSerializable(typeof(CallResult))]
[JsonSerializable(typeof(RpcErrorBody))]
public partial class McpJsonContext : JsonSerializerContext;
