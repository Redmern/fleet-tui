namespace Fleet.Ports.Remotes.Models;

public sealed record RemoteProjectRequest(string Name, string Path, bool CreateDirectory, bool Open);
