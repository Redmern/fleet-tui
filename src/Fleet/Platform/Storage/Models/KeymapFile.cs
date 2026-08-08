namespace Fleet.Platform.Storage.Models;

public sealed class KeymapFile
{
    public int Version { get; set; } = 1;

    public string Prefix { get; set; } = string.Empty;

    public Dictionary<string, string> Bindings { get; set; } = [];
}
