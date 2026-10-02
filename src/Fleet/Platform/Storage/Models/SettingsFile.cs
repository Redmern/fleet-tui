using System.Text.Json.Serialization;

namespace Fleet.Platform.Storage.Models;

public sealed class SettingsFile
{
    public int Version { get; set; } = 1;

    public string Trigger { get; set; } = string.Empty;

    public string Commit { get; set; } = string.Empty;

    public string Push { get; set; } = string.Empty;

    public string Aidlc { get; set; } = string.Empty;

    public string AidlcProfile { get; set; } = string.Empty;

    public string AidlcAutonomy { get; set; } = string.Empty;

    public List<string> AidlcOff { get; set; } = [];

    public string MainOrchestratorInNvim { get; set; } = string.Empty;

    public string SubOrchestratorsInNvim { get; set; } = string.Empty;

    public string AutoClose { get; set; } = string.Empty;

    public int AutoCloseMinutes { get; set; }

    public Dictionary<string, ToolRuleEntry> Tools { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? StatusHooks { get; set; }
}

public sealed class ToolRuleEntry
{
    public string Policy { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;
}
