namespace Fleet.Platform.Uninstall.Models;

public sealed class DependencyEntryJson
{
    public string? Tool { get; set; }

    public string? Manager { get; set; }

    public string? Package { get; set; }

    public bool? Sudo { get; set; }

    public string? InstalledAt { get; set; }
}
