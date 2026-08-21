using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;

namespace Fleet.Platform.Mux;

public sealed class FailSilentDriver(IMuxDriver inner, Action<Exception> onSwallowed) : IMuxDriver
{
    public string Name => inner.Name;

    public MuxCaps Caps => inner.Caps;

    public PaneId CurrentPane => inner.CurrentPane;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => Guard(() => inner.IsAvailableAsync(ct), false);

    public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default)
        => Guard(() => inner.ListPanesAsync(ct), Array.Empty<Pane>());

    public Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
        => Guard(() => inner.SpawnAsync(options, ct), PaneId.None);

    public Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default)
        => Guard(() => inner.SplitAsync(options, ct), PaneId.None);

    public Task KillPaneAsync(PaneId id, CancellationToken ct = default)
        => Guard(() => inner.KillPaneAsync(id, ct));

    public Task MovePaneAsync(PaneId id, MovePaneOptions options, CancellationToken ct = default)
        => Guard(() => inner.MovePaneAsync(id, options, ct));

    public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
        => Guard(() => inner.SetTitleAsync(id, title, ct));

    public Task FocusPaneAsync(PaneId id, CancellationToken ct = default)
        => Guard(() => inner.FocusPaneAsync(id, ct));

    public Task SendTextAsync(PaneId id, string text, CancellationToken ct = default)
        => Guard(() => inner.SendTextAsync(id, text, ct));

    public Task<string> GetTextAsync(PaneId id, CancellationToken ct = default)
        => Guard(() => inner.GetTextAsync(id, ct), string.Empty);

    private static bool IsExpected(Exception e) =>
        e is MuxUnavailableException
          or IOException
          or TimeoutException
          or OperationCanceledException
          or System.ComponentModel.Win32Exception;

    private async Task<T> Guard<T>(Func<Task<T>> call, T fallback)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception e) when (IsExpected(e))
        {
            onSwallowed(e);
            return fallback;
        }
    }

    private async Task Guard(Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (Exception e) when (IsExpected(e))
        {
            onSwallowed(e);
        }
    }
}
