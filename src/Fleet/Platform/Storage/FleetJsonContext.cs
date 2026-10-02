using System.Text.Json.Serialization;
using Fleet.Platform.Approvals.Models;
using Fleet.Platform.Storage.Models;

namespace Fleet.Platform.Storage;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProjectFile))]
[JsonSerializable(typeof(KeymapFile))]
[JsonSerializable(typeof(KnownRemotesFile))]
[JsonSerializable(typeof(SessionFile))]
[JsonSerializable(typeof(SettingsFile))]
[JsonSerializable(typeof(AskFile))]
[JsonSerializable(typeof(NoticeFile))]
[JsonSerializable(typeof(NoticeSettingsFile))]
[JsonSerializable(typeof(WindowSessionFile))]
public partial class FleetJsonContext : JsonSerializerContext;
