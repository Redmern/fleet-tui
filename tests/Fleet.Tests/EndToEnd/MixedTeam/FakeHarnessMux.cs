using System.Collections.Concurrent;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Harness;

namespace Fleet.Tests.EndToEnd.MixedTeam;

// The fake mux plus a fake harness in every pane whose command is a registered harness binary.
// Everything else is the plain FakeMuxDriver.
internal sealed class FakeHarnessMux : IMuxDriver
{
    private readonly ConcurrentDictionary<string, FakeHarness> _harnesses = new();

    public FakeMuxDriver Inner { get; } = new();

    public string Name => Inner.Name;

    public MuxCaps Caps => Inner.Caps;

    public PaneId CurrentPane => Inner.CurrentPane;

    public FakeHarness HarnessIn(string folder) =>
        _harnesses.Values.Single(h => PathKey.Same(h.Folder, folder));

    public IReadOnlyList<string> TypedInto(FakeHarness harness) => Inner.SentTo(harness.Pane);

    public IReadOnlyList<string> ArgsOf(FakeHarness harness) => Inner.ArgsFor(harness.Pane);

    public async Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
    {
        var pane = await Inner.SpawnAsync(options, ct);

        if (HarnessRegistry.Running(options.Args) is { } profile && options.Cwd is { } folder)
        {
            _harnesses[pane.Value] = FakeHarness.Start(Inner, pane, profile, folder);
        }

        return pane;
    }

    public async Task SendTextAsync(PaneId id, string text, CancellationToken ct = default)
    {
        await Inner.SendTextAsync(id, text, ct);

        if (_harnesses.TryGetValue(id.Value, out var harness))
        {
            harness.Key(text);
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Inner.IsAvailableAsync(ct);

    public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default) => Inner.ListPanesAsync(ct);

    public Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default) =>
        Inner.SplitAsync(options, ct);

    public Task<PaneId> SpawnFloatingAsync(PaneId over, SpawnOptions options, CancellationToken ct = default) =>
        Inner.SpawnFloatingAsync(over, options, ct);

    public Task KillPaneAsync(PaneId id, CancellationToken ct = default) => Inner.KillPaneAsync(id, ct);

    public Task MovePaneAsync(PaneId id, MovePaneOptions options, CancellationToken ct = default) =>
        Inner.MovePaneAsync(id, options, ct);

    public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default) =>
        Inner.SetTitleAsync(id, title, ct);

    public Task FocusPaneAsync(PaneId id, CancellationToken ct = default) => Inner.FocusPaneAsync(id, ct);

    public Task<string> GetTextAsync(PaneId id, CancellationToken ct = default) => Inner.GetTextAsync(id, ct);

    public Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken ct = default) =>
        Inner.ListWorkspacesAsync(ct);

    public Task ShowWorkspaceAsync(string name, CancellationToken ct = default) => Inner.ShowWorkspaceAsync(name, ct);

    public Task CloseWorkspaceAsync(string name, CancellationToken ct = default) =>
        Inner.CloseWorkspaceAsync(name, ct);

    public Task OpenWindowAsync(string name, CancellationToken ct = default) => Inner.OpenWindowAsync(name, ct);
}
