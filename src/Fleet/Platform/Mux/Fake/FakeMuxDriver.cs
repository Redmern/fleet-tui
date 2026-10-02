using System.Collections.Concurrent;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.Fake;

public sealed class FakeMuxDriver(bool workspaces = false) : IMuxDriver
{
    private readonly ConcurrentDictionary<string, string> _showing = new();
    private readonly ConcurrentQueue<string> _calls = new();
    private readonly ConcurrentDictionary<string, Entry> _panes = new();
    private readonly ConcurrentDictionary<string, string> _tabTitles = new();
    private readonly ConcurrentDictionary<string, List<string>> _sent = new();

    private readonly ConcurrentDictionary<string, string> _text = new();
    private int _nextPane;
    private int _nextWindow;
    private int _nextTab;

    public string Name => "fake";

    public MuxCaps Caps => MuxCaps.Split | MuxCaps.Zoom | MuxCaps.Persist
        | (workspaces ? MuxCaps.Workspaces | MuxCaps.Detach | MuxCaps.Popup : MuxCaps.None);

    public string CurrentClient { get; set; } = "c1";

    public IReadOnlyList<string> Calls => [.. _calls];

    public string? ShowingFor(string client) =>
        _showing.TryGetValue(client, out var name) ? name : null;

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
        _calls.Enqueue("spawn");

        var id = NextPaneId();

        if (workspaces)
        {
            var name = options.Workspace
                ?? options.SessionName
                ?? (options.NewWindow ? null : options.WindowId)
                ?? "default";

            Add(id, name, NextTabId(), name, options.Cwd ?? string.Empty, options.Args, options.Env);
            return Task.FromResult(id);
        }

        var window = options.NewWindow
            ? NextWindowId()
            : _panes.Values.FirstOrDefault()?.Pane.WindowId ?? NextWindowId();

        Add(
            id,
            window,
            NextTabId(),
            options.Workspace ?? options.SessionName ?? "default",
            options.Cwd ?? string.Empty,
            options.Args,
            options.Env);
        return Task.FromResult(id);
    }

    public Task<PaneId> SpawnFloatingAsync(PaneId over, SpawnOptions options, CancellationToken ct = default)
    {
        RequireAvailable();
        _calls.Enqueue("spawn-float");

        if (!workspaces)
        {
            throw new NotSupportedException("this fake has no floating panes");
        }

        var near = over.IsNone ? CurrentPane : over;
        if (!_panes.TryGetValue(near.Value, out var below))
        {
            throw new MuxUnavailableException($"no pane {near}");
        }

        var session = options.Workspace ?? options.SessionName ?? below.Pane.SessionName;
        var id = NextPaneId();
        Add(id, session, "float", session, options.Cwd ?? below.Pane.Cwd, options.Args, options.Env);
        return Task.FromResult(id);
    }

    public Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default)
    {
        RequireAvailable();
        _calls.Enqueue("split");

        if (!_panes.TryGetValue(options.Source.Value, out var source))
        {
            throw new MuxUnavailableException($"no pane {options.Source}");
        }

        if (!options.MovePane.IsNone)
        {
            RequirePane(options.MovePane);

            Mutate(options.MovePane, p => p with
            {
                WindowId = source.Pane.WindowId,
                TabId = source.Pane.TabId,
                SessionName = source.Pane.SessionName,
            });

            return Task.FromResult(options.MovePane);
        }

        var id = NextPaneId();
        Add(
            id,
            source.Pane.WindowId,
            source.Pane.TabId,
            source.Pane.SessionName,
            options.Cwd ?? source.Pane.Cwd,
            options.Args,
            options.Env);

        return Task.FromResult(id);
    }

    public Task KillPaneAsync(PaneId id, CancellationToken ct = default)
    {
        RequireAvailable();
        _calls.Enqueue("kill");
        _panes.TryRemove(id.Value, out _);

        return Task.CompletedTask;
    }

    public Task MovePaneAsync(
        PaneId id, MovePaneOptions options, CancellationToken ct = default)
    {
        RequireAvailable();
        RequirePane(id);
        _calls.Enqueue("move");

        if (workspaces)
        {
            var target = options.Workspace ?? options.WindowId ?? _panes[id.Value].Pane.SessionName;

            Mutate(id, p => p with { SessionName = target, WindowId = target, TabId = NextTabId() });
            return Task.CompletedTask;
        }

        var inherited = options.WindowId is { } window
            ? _panes.Values
                .Select(e => e.Pane)
                .FirstOrDefault(p => p.Id != id && p.WindowId == window)?.SessionName
            : null;

        Mutate(id, p => p with
        {
            SessionName = options.Workspace ?? inherited ?? p.SessionName,
            WindowId = options.WindowId ?? (options.NewWindow ? NextWindowId() : p.WindowId),
            TabId = NextTabId(),
        });

        return Task.CompletedTask;
    }

    public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
    {
        RequireAvailable();
        RequirePane(id);

        _tabTitles[_panes[id.Value].Pane.TabId] = title;

        foreach (var key in _panes.Keys)
        {
            Mutate(new PaneId(key), p => p);
        }

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

    public Task SendTextAsync(PaneId id, string text, CancellationToken ct = default)
    {
        RequireAvailable();
        RequirePane(id);

        _sent.GetOrAdd(id.Value, _ => []).Add(text);

        return Task.CompletedTask;
    }

    public Task<string> GetTextAsync(PaneId id, CancellationToken ct = default)
    {
        RequireAvailable();
        RequirePane(id);

        return Task.FromResult(_text.TryGetValue(id.Value, out var text) ? text : string.Empty);
    }

    public Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        RequireAvailable();

        if (!workspaces)
        {
            return Task.FromResult<IReadOnlyList<Workspace>>([]);
        }

        var shown = ShowingFor(CurrentClient);

        IReadOnlyList<Workspace> list = _panes.Values
            .Select(e => e.Pane.SessionName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(n => new Workspace(n, string.Equals(n, shown, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return Task.FromResult(list);
    }

    public Task ShowWorkspaceAsync(string name, CancellationToken ct = default)
    {
        RequireAvailable();
        _calls.Enqueue("show");

        if (!workspaces)
        {
            throw new NotSupportedException("this fake has no workspaces");
        }

        if (!_panes.Values.Any(e =>
                string.Equals(e.Pane.SessionName, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MuxUnavailableException($"no workspace {name}");
        }

        _showing[CurrentClient] = name;
        return Task.CompletedTask;
    }

    public Task OpenWindowAsync(string name, CancellationToken ct = default)
    {
        RequireAvailable();
        _calls.Enqueue($"open-window {name}");
        return Task.CompletedTask;
    }

    public Task CloseWorkspaceAsync(string name, CancellationToken ct = default)
    {
        RequireAvailable();
        _calls.Enqueue("close");

        foreach (var e in _panes.Values.Where(e =>
                     string.Equals(e.Pane.SessionName, name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _panes.TryRemove(e.Pane.Id.Value, out _);
        }

        foreach (var client in _showing.Where(kv =>
                     string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _showing.TryRemove(client.Key, out _);
        }

        return Task.CompletedTask;
    }

    public void SetText(PaneId id, string text)
    {
        _text[id.Value] = text;
    }

    public void SetPaneTitle(PaneId id, string title)
    {
        RequirePane(id);
        Mutate(id, p => p with { PaneTitle = title });
    }

    public IReadOnlyList<string> SentTo(PaneId id) =>
        _sent.TryGetValue(id.Value, out var lines) ? lines : [];

    public IReadOnlyList<string> ArgsFor(PaneId id) =>
        _panes.TryGetValue(id.Value, out var e) ? e.Args : [];

    public string TitleOf(PaneId id) =>
        _panes.TryGetValue(id.Value, out var e) ? e.Pane.Title : string.Empty;

    public IReadOnlyDictionary<string, string> EnvFor(PaneId id) =>
        _panes.TryGetValue(id.Value, out var e) ? e.Env : new Dictionary<string, string>();

    private PaneId NextPaneId() => new($"p{Interlocked.Increment(ref _nextPane)}");

    private string NextWindowId() => $"w{Interlocked.Increment(ref _nextWindow)}";

    private string NextTabId() => $"t{Interlocked.Increment(ref _nextTab)}";

    private void Add(
        PaneId id, string window, string tab, string session, string cwd, IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null)
    {
        _panes[id.Value] = new Entry(
            new Pane(
                id,
                window,
                TabId: tab,
                SessionName: session,
                Title: string.Empty,
                Cwd: cwd,
                IsActive: true,
                PaneTitle: AgentHarness.TitledPaneTitle(args) ?? string.Empty),
            args,
            env ?? new Dictionary<string, string>());

        Mutate(id, p => p);
    }

    private void Mutate(PaneId id, Func<Pane, Pane> change)
    {
        if (_panes.TryGetValue(id.Value, out var e))
        {
            var next = change(e.Pane);
            var tabTitle = _tabTitles.TryGetValue(next.TabId, out var t) ? t : string.Empty;

            _panes[id.Value] = e with
            {
                Pane = next with { Title = tabTitle.Length > 0 ? tabTitle : next.PaneTitle },
            };
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

    private sealed record Entry(
        Pane Pane, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env);
}
