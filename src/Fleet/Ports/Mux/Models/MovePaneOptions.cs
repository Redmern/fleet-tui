namespace Fleet.Ports.Mux.Models;

public sealed record MovePaneOptions
{
    public string? Workspace { get; init; }

    public string? WindowId { get; init; }

    public bool NewWindow { get; init; }
}
