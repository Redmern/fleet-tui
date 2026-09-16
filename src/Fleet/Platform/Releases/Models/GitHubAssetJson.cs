using System.Text.Json.Serialization;

namespace Fleet.Platform.Releases.Models;

public sealed class GitHubAssetJson
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}
