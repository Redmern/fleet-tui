namespace Fleet.Platform.Storage.Models;

public sealed class SettingsFile
{
    public int Version { get; set; } = 1;

    public string Trigger { get; set; } = string.Empty;

    public string Commit { get; set; } = string.Empty;

    public string Push { get; set; } = string.Empty;

    public string Aidlc { get; set; } = string.Empty;

    public Dictionary<string, ToolRuleEntry> Tools { get; set; } = [];
}

public sealed class ToolRuleEntry
{
    public string Policy { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;
}
