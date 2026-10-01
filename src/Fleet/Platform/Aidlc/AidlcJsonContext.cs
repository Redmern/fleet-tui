using System.Text.Json.Serialization;
using Fleet.Platform.Aidlc.Models;

namespace Fleet.Platform.Aidlc;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntentStateFile))]
[JsonSerializable(typeof(AuditLine))]
public partial class AidlcJsonContext : JsonSerializerContext;
