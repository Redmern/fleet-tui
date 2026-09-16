using System.Text.Json.Serialization;
using Fleet.Platform.Releases.Models;

namespace Fleet.Platform.Releases;

[JsonSerializable(typeof(GitHubReleaseJson))]
public partial class GitHubJsonContext : JsonSerializerContext;
