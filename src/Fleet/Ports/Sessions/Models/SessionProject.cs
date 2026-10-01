namespace Fleet.Ports.Sessions.Models;

public sealed record SessionProject(string Name, string? Host = null)
{
    public bool IsRemote => Host is not null;
}