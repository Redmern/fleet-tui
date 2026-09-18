using Fleet.Platform.Mux.WezTerm;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;

namespace Fleet.Platform.Mux;

public sealed class AutoStartDriver(
    IMuxDriver inner, WezTermInstanceLauncher launcher, string configFile, TimeSpan timeout)
    : IMuxDriver
{
    private bool _ensured;

    public string Name => inner.Name;

    public MuxCaps Caps => inner.Caps;

    public PaneId CurrentPane => inner.CurrentPane;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        return await inner.IsAvailableAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        return await inner.ListPanesAsync(ct).ConfigureAwait(false);
    }

    public async Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        return await inner.SpawnAsync(options, ct).ConfigureAwait(false);
    }

    public async Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        return await inner.SplitAsync(options, ct).ConfigureAwait(false);
    }

    public async Task KillPaneAsync(PaneId id, CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        await inner.KillPaneAsync(id, ct).ConfigureAwait(false);
    }

    public async Task MovePaneAsync(
        PaneId id, MovePaneOptions options, CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        await inner.MovePaneAsync(id, options, ct).ConfigureAwait(false);
    }

    public async Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        await inner.SetTitleAsync(id, title, ct).ConfigureAwait(false);
    }

    public async Task FocusPaneAsync(PaneId id, CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        await inner.FocusPaneAsync(id, ct).ConfigureAwait(false);
    }

    public async Task SendTextAsync(PaneId id, string text, CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        await inner.SendTextAsync(id, text, ct).ConfigureAwait(false);
    }

    public async Task<string> GetTextAsync(PaneId id, CancellationToken ct = default)
    {
        await EnsureAsync(ct).ConfigureAwait(false);
        return await inner.GetTextAsync(id, ct).ConfigureAwait(false);
    }

    private async Task EnsureAsync(CancellationToken ct)
    {
        if (_ensured)
        {
            return;
        }

        _ensured = true;
        await launcher.EnsureRunningAsync(configFile, timeout, ct).ConfigureAwait(false);
    }
}
