namespace Fleet.Ports.Projects.Models;

public sealed record Project(
    string Name,
    string Root,
    string? ClaudeProfile = null,
    IReadOnlyList<int>? ForwardPorts = null,
    string? RunCommand = null,
    int? ReadyPort = null,
    string? HealthPath = null)
{
    public IReadOnlyList<int> Forwarded => ForwardPorts ?? [];
}
