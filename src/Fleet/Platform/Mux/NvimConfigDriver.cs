using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux;

public sealed class NvimConfigDriver(IMuxDriver inner, Func<bool> useFleetConfig) : IMuxDriver
{
    public string Name => inner.Name;

    public MuxCaps Caps => inner.Caps;

    public PaneId CurrentPane => inner.CurrentPane;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => inner.IsAvailableAsync(ct);

    public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default) => inner.ListPanesAsync(ct);

    public Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default) =>
        inner.SpawnAsync(options with { Env = EnvFor(options.Args, options.Env) }, ct);

    public Task<PaneId> SpawnFloatingAsync(PaneId over, SpawnOptions options, CancellationToken ct = default) =>
        inner.SpawnFloatingAsync(over, options with { Env = EnvFor(options.Args, options.Env) }, ct);

    public Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default) =>
        inner.SplitAsync(options with { Env = EnvFor(options.Args, options.Env) }, ct);

    public Task KillPaneAsync(PaneId id, CancellationToken ct = default) => inner.KillPaneAsync(id, ct);

    public Task MovePaneAsync(PaneId id, MovePaneOptions options, CancellationToken ct = default) =>
        inner.MovePaneAsync(id, options, ct);

    public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default) =>
        inner.SetTitleAsync(id, title, ct);

    public Task FocusPaneAsync(PaneId id, CancellationToken ct = default) => inner.FocusPaneAsync(id, ct);

    public Task SendTextAsync(PaneId id, string text, CancellationToken ct = default) =>
        inner.SendTextAsync(id, text, ct);

    public Task<string> GetTextAsync(PaneId id, CancellationToken ct = default) => inner.GetTextAsync(id, ct);

    public Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken ct = default) =>
        inner.ListWorkspacesAsync(ct);

    public Task ShowWorkspaceAsync(string name, CancellationToken ct = default) =>
        inner.ShowWorkspaceAsync(name, ct);

    public Task CloseWorkspaceAsync(string name, CancellationToken ct = default) =>
        inner.CloseWorkspaceAsync(name, ct);

    public Task OpenWindowAsync(string name, CancellationToken ct = default) => inner.OpenWindowAsync(name, ct);

    private IReadOnlyDictionary<string, string> EnvFor(
        IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env) =>
        AgentHarness.LaunchesNvim(args) && useFleetConfig() ? AgentHarness.WithFleetNvimConfig(args, env) : env;
}
