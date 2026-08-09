using System.Text.Json.Serialization;
using Fleet.Platform.Storage.Models;

namespace Fleet.Platform.Storage;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProjectFile))]
[JsonSerializable(typeof(KeymapFile))]
[JsonSerializable(typeof(SessionFile))]
public partial class FleetJsonContext : JsonSerializerContext;
