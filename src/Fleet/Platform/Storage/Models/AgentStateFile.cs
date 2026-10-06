namespace Fleet.Platform.Storage.Models;

public sealed class AgentStateFile
{
    public int Version { get; set; } = 1;

    public string Worktree { get; set; } = string.Empty;

    public string Session { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTime At { get; set; }

    public string Transcript { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Inbox { get; set; } = string.Empty;
}
