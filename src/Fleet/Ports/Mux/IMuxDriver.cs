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

    Task KillPaneAsync(PaneId id, CancellationToken ct = default);

    Task MovePaneAsync(PaneId id, MovePaneOptions options, CancellationToken ct = default);

    Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default);

    Task FocusPaneAsync(PaneId id, CancellationToken ct = default);

    Task SendTextAsync(PaneId id, string text, CancellationToken ct = default);

    Task<string> GetTextAsync(PaneId id, CancellationToken ct = default);
}
