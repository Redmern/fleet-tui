namespace Fleet.Platform.Storage.Models;

public sealed class ProjectFile
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public string Root { get; set; } = string.Empty;
}
