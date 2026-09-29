namespace Fleet.Platform.Storage.Models;

public sealed class WindowSessionFile
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public List<WindowSessionProject> Projects { get; set; } = [];

    public WindowSessionProject? Showing { get; set; }
}

public sealed class WindowSessionProject
{
    public string Name { get; set; } = string.Empty;

    public string? Host { get; set; }
}