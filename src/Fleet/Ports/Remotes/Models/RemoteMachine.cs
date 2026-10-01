using Fleet.Ports.Remotes.Enums;

namespace Fleet.Ports.Remotes.Models;

public sealed record RemoteMachine(
    string Host,
    string Name,
    RemoteState State,
    IReadOnlyList<string> Projects,
    string? Error = null,
    string? Prompt = null,
    bool Secret = true,
    IReadOnlyList<string>? Running = null)
{
    public bool IsRunning(string project) =>
        Running?.Contains(project, StringComparer.OrdinalIgnoreCase) == true;
}