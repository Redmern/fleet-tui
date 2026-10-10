using System.Text.Json.Serialization;
using Fleet.Platform.Storage.Models;

namespace Fleet.Platform.Storage;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IsoFile))]
public partial class IsoJsonContext : JsonSerializerContext;
