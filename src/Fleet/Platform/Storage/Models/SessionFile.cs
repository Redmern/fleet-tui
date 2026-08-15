namespace Fleet.Platform.Storage.Models;

public sealed class SessionFile
{
    public int Version { get; set; } = 1;

    public List<AgentEntry> Agents { get; set; } = [];
}

public sealed class AgentEntry
{
    public string Worktree { get; set; } = string.Empty;

    public string Repository { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    public string Harness { get; set; } = string.Empty;

    public string BaseRef { get; set; } = string.Empty;

    public bool RepositoryWasBare { get; set; }

    public bool Hidden { get; set; }

    public bool Open { get; set; }

    public string Owner { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}
