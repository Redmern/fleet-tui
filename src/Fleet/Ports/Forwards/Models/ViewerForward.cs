namespace Fleet.Ports.Forwards.Models;

public sealed record ViewerForward(string? Viewer, string? SshConnection)
{
    public bool Sent => Viewer is not null;
}
