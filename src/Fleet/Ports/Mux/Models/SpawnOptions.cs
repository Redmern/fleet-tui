namespace Fleet.Ports.Mux.Models;

public sealed record SpawnOptions
{
    public string? Cwd { get; init; }

    public string? SessionName { get; init; }

    public bool NewWindow { get; init; }

    public string? Workspace { get; init; }

    public IReadOnlyList<string> Args { get; init; } = [];
}
