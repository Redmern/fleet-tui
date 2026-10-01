namespace Fleet.Platform.Storage.Models;

public sealed class KnownRemotesFile
{
    public int Version { get; set; } = 1;

    public List<KnownRemoteEntry> Remotes { get; set; } = [];
}

public sealed class KnownRemoteEntry
{
    public string Host { get; set; } = string.Empty;

    public string? Nickname { get; set; }

    public DateTimeOffset LastConnected { get; set; }
}
