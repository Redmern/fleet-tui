using System.Text.Json.Serialization;

namespace Fleet.Platform.Mux.WezTerm.Models;

public sealed class WezTermPaneJson
{
    [JsonPropertyName("window_id")]
    public int WindowId { get; set; }

    [JsonPropertyName("tab_id")]
    public int TabId { get; set; }

    [JsonPropertyName("pane_id")]
    public int PaneId { get; set; }

    [JsonPropertyName("workspace")]
    public string Workspace { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("tab_title")]
    public string TabTitle { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }
}
