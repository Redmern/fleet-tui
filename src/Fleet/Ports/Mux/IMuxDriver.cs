using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;

namespace Fleet.Ports.Mux;

public interface IMuxDriver
{
    string Name { get; }

    MuxCaps Caps { get; }

    PaneId CurrentPane { get; }

    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default);

    Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default);

    Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default);

    Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default);

    Task FocusPaneAsync(PaneId id, CancellationToken ct = default);
}
