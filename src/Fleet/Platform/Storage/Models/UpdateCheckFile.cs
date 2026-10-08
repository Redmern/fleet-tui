namespace Fleet.Platform.Storage.Models;

public sealed class UpdateCheckFile
{
    public int Version { get; set; } = 1;

    public string Latest { get; set; } = string.Empty;

    public DateTimeOffset CheckedAt { get; set; }
}
