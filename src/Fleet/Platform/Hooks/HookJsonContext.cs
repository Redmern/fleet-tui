using System.Text.Json.Serialization;
using Fleet.Platform.Hooks.Models;

namespace Fleet.Platform.Hooks;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HookPayload))]
[JsonSerializable(typeof(HookBlock))]
public partial class HookJsonContext : JsonSerializerContext;
