using System.Text.Json.Serialization;

namespace Fleet.Platform.Storage.Models;

public sealed class SettingsFile
{
    public int Version { get; set; } = 1;

    public string Trigger { get; set; } = string.Empty;

    public string Commit { get; set; } = string.Empty;

    public string Push { get; set; } = string.Empty;

    public string Merge { get; set; } = string.Empty;

    public string Aidlc { get; set; } = string.Empty;

    public string AidlcProfile { get; set; } = string.Empty;

    public string AidlcAutonomy { get; set; } = string.Empty;

    public List<string> AidlcOff { get; set; } = [];

    public string MainOrchestratorInNvim { get; set; } = string.Empty;

    public string SubOrchestratorsInNvim { get; set; } = string.Empty;

    public string AutoClose { get; set; } = string.Empty;

    public int AutoCloseMinutes { get; set; }

    public string MainModel { get; set; } = string.Empty;

    public string MainEffort { get; set; } = string.Empty;

    public string SubModel { get; set; } = string.Empty;

    public string SubEffort { get; set; } = string.Empty;

    public string AgentModel { get; set; } = string.Empty;

    public string AgentEffort { get; set; } = string.Empty;

    public Dictionary<string, ToolRuleEntry> Tools { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? StatusHooks { get; set; }
}

public sealed class HeadSettingsFile
{
    public int Version { get; set; } = 1;

    public string Model { get; set; } = string.Empty;

    public string Effort { get; set; } = string.Empty;
}

public sealed class ToolRuleEntry
{
    public string Policy { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;
}
