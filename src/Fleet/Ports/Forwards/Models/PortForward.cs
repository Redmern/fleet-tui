using Fleet.Ports.Forwards.Enums;

namespace Fleet.Ports.Forwards.Models;

public sealed record PortForward(
    string Host,
    int RemotePort,
    int? LocalPort,
    ForwardState State,
    string? Project = null,
    string? Error = null,
    bool Viewer = false)
{
    public string? Url => State == ForwardState.Forwarded && LocalPort is { } local ? $"http://localhost:{local}" : null;
}
