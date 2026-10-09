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

    public string Describe()
    {
        var where = Viewer ? $"forwarded to {Host}" : Host;
        var state = State switch
        {
            ForwardState.Forwarded => $"localhost:{LocalPort}",
            ForwardState.Detected => "listening, not forwarded",
            ForwardState.Waiting => "waiting",
            _ => "failed",
        };

        return $"{where}  {RemotePort} -> {state}"
            + (Project is { } project ? $"  ({project})" : string.Empty)
            + (Error is { } error ? $"  · {error}" : string.Empty);
    }
}
