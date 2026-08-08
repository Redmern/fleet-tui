using Fleet.Ports.Mux;

namespace Fleet.Platform.Mux;

/// <summary>
/// Enforces fleet's fail-silent invariant once, instead of at every call site.
///
/// Reads degrade to empty results and writes become no-ops when the multiplexer
/// is unreachable. C# propagates exceptions by default, which is exactly
/// backwards for this invariant, so command code is only ever handed a wrapped
/// driver.
///
/// Three carve-outs, because "swallow everything" is its own bug:
/// <list type="bullet">
///   <item>Only expected failure types are caught. A programmer error still
///   propagates: a bad argument is a defect, not a closed terminal.</item>
///   <item>Everything swallowed is reported to <paramref name="onSwallowed"/>, so
///   silent never means invisible.</item>
///   <item>Destructive operations must NOT be routed through this. A teardown
///   that silently "succeeds" while the worktree is still on disk is how state
///   diverges from reality.</item>
/// </list>
/// </summary>
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

    public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
        => Guard(() => inner.SetTitleAsync(id, title, ct));

    public Task FocusPaneAsync(PaneId id, CancellationToken ct = default)
        => Guard(() => inner.FocusPaneAsync(id, ct));

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
