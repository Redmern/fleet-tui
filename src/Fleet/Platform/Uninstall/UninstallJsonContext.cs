using System.Text.Json.Serialization;
using Fleet.Platform.Uninstall.Models;

namespace Fleet.Platform.Uninstall;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DependencyManifestJson))]
public partial class UninstallJsonContext : JsonSerializerContext;
