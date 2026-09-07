using Fleet.Ports.Mux.Enums;

namespace Fleet.Ports.Mux.Models;

public sealed record SplitOptions(PaneId Source, SplitDirection Direction)
{
    public int Percent { get; init; }

    public string? Cwd { get; init; }

    public IReadOnlyList<string> Args { get; init; } = [];

    public PaneId MovePane { get; init; } = PaneId.None;
}
