namespace Fleet.Platform.Forge;

public sealed class ForgeCacheFile
{
    public int Version { get; set; } = 1;

    public Dictionary<string, string> Hosts { get; set; } = [];
}
