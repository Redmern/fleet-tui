using System.Text.Json.Serialization;

namespace Fleet.Platform.Forge;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ForgeCacheFile))]
public partial class ForgeJsonContext : JsonSerializerContext;
