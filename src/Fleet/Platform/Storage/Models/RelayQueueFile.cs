namespace Fleet.Platform.Storage.Models;

public sealed class RelayQueueFile
{
    public int Version { get; set; } = 1;

    public string Project { get; set; } = string.Empty;

    public List<RelayQueueEntry> Prompts { get; set; } = [];
}

public sealed class RelayQueueEntry
{
    public string Text { get; set; } = string.Empty;

    public DateTimeOffset QueuedAt { get; set; }
}
