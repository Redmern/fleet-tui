using System.Text.Json.Serialization;
using Fleet.Platform.Claude.Models;

namespace Fleet.Platform.Claude;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpJsonFile))]
[JsonSerializable(typeof(ClaudeSettingsFile))]
[JsonSerializable(typeof(UserSettingsFile))]
public partial class ClaudeJsonContext : JsonSerializerContext;
