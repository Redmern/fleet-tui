using System.Collections.Concurrent;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;

namespace Fleet.Platform.Mux.Fake;

public sealed class FakeMuxDriver : IMuxDriver
{
    private readonly ConcurrentDictionary<string, Entry> _panes = new();
    private int _nextPane;
    private int _nextWindow;

    public string Name => "fake";

    public MuxCaps Caps => MuxCaps.Split | MuxCaps.Zoom | MuxCaps.Persist;

    public bool Available { get; set; } = true;

    public PaneId CurrentPane { get; set; } = PaneId.None;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(Available);

    public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default)
    {
        RequireAvailable();

        IReadOnlyList<Pane> panes = _panes.Values
            .Select(e => e.Pane)
            .OrderBy(p => p.Id.Value.Length)
            .ThenBy(p => p.Id.Value, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(panes);
    }

    public Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
    {
        RequireAvailable();

        var id = NextPaneId();

        var window = options.NewWindow
            ? NextWindowId()
            : _panes.Values.FirstOrDefault()?.Pane.WindowId ?? NextWindowId();

        Add(
            id,
            window,
            options.Workspace ?? options.SessionName ?? "default",
            options.Cwd ?? string.Empty,
            options.Args);
        return Task.FromResult(id);
    }

    public Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default)
    {
        RequireAvailable();

        if (!_panes.TryGetValue(options.Source.Value, out var source))
        {
            throw new MuxUnavailableException($"no pane {options.Source}");
        }

        var id = NextPaneId();
        Add(
            id,
            source.Pane.WindowId,
            source.Pane.SessionName,
            options.Cwd ?? source.Pane.Cwd,
            options.Args);

        return Task.FromResult(id);
    }

    public Task KillPaneAsync(PaneId id, CancellationToken ct = default)
    {
        RequireAvailable();
        _panes.TryRemove(id.Value, out _);

        return Task.CompletedTask;
    }

    public Task MovePaneAsync(
        PaneId id, MovePaneOptions options, CancellationToken ct = default)
    {
        RequireAvailable();
        RequirePane(id);

        Mutate(id, p => p with
        {
            SessionName = options.Workspace ?? p.SessionName,
            WindowId = options.WindowId ?? (options.NewWindow ? NextWindowId() : p.WindowId),
        });

        return Task.CompletedTask;
    }

    public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
    {
        RequireAvailable();
        RequirePane(id);
        Mutate(id, p => p with { Title = title });
        return Task.CompletedTask;
    }

    public Task FocusPaneAsync(PaneId id, CancellationToken ct = default)
    {
        RequireAvailable();
        RequirePane(id);

        foreach (var key in _panes.Keys)
        {
            Mutate(new PaneId(key), p => p with { IsActive = key == id.Value });
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<string> ArgsFor(PaneId id) =>
        _panes.TryGetValue(id.Value, out var e) ? e.Args : [];

    public string TitleOf(PaneId id) =>
        _panes.TryGetValue(id.Value, out var e) ? e.Pane.Title : string.Empty;

    private PaneId NextPaneId() => new($"p{Interlocked.Increment(ref _nextPane)}");

    private string NextWindowId() => $"w{Interlocked.Increment(ref _nextWindow)}";

    private void Add(
        PaneId id, string window, string session, string cwd, IReadOnlyList<string> args)
        => _panes[id.Value] = new Entry(
            new Pane(
                id,
                window,
                TabId: id.Value,
                SessionName: session,
                Title: string.Empty,
                Cwd: cwd,
                IsActive: true),
            args);

    private void Mutate(PaneId id, Func<Pane, Pane> change)
    {
        if (_panes.TryGetValue(id.Value, out var e))
        {
            _panes[id.Value] = e with { Pane = change(e.Pane) };
        }
    }

    private void RequireAvailable()
    {
        if (!Available)
        {
            throw new MuxUnavailableException("fake mux is marked unavailable");
        }
    }

    private void RequirePane(PaneId id)
    {
        if (!_panes.ContainsKey(id.Value))
        {
            throw new MuxUnavailableException($"no pane {id}");
        }
    }

    private sealed record Entry(Pane Pane, IReadOnlyList<string> Args);
}
