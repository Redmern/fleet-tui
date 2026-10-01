namespace Fleet.Ports.Remotes.Models;

public sealed record KnownRemote(string Host, string? Nickname, DateTimeOffset LastConnected)
{
    public string Label => Nickname ?? Host;
}
