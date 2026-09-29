namespace Fleet.Platform.Storage.Models;

public sealed class NoticeFile
{
    public int Version { get; set; } = 1;

    public List<NoticeEntry> Notices { get; set; } = [];
}

public sealed class NoticeEntry
{
    public string Kind { get; set; } = string.Empty;

    public string Worktree { get; set; } = string.Empty;

    public string Agent { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime Since { get; set; }

    public DateTime? Resolved { get; set; }

    public DateTime? Dismissed { get; set; }
}

public sealed class NoticeSettingsFile
{
    public bool Bell { get; set; }

    public bool Toast { get; set; } = true;
}