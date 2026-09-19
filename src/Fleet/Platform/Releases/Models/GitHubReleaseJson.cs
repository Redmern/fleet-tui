using System.Text.Json.Serialization;

namespace Fleet.Platform.Releases.Models;

public sealed class GitHubReleaseJson
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public GitHubAssetJson[] Assets { get; set; } = [];

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }
}
