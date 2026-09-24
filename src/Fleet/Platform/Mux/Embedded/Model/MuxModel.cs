using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.Embedded.Model;

public sealed class MuxModel
{
    public const int StatusRows = 1;

    private readonly List<WorkspaceState> _workspaces = [];
    private readonly Dictionary<string, PaneState> _panes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClientState> _clients = new(StringComparer.Ordinal);
    private int _nextPane;
    private int _nextTab;
    private int _nextClient;
    private long _clock;

    public (int Cols, int Rows) DefaultSize { get; set; } = (120, 40);

    public IReadOnlyCollection<PaneState> Panes => _panes.Values;

    public IReadOnlyCollection<ClientState> Clients => _clients.Values;

    public PaneState? Pane(string id) => _panes.GetValueOrDefault(id);

    public ClientState? Client(string id) => _clients.GetValueOrDefault(id);

    public WorkspaceState? Workspace(string name) =>
        _workspaces.FirstOrDefault(w => Same(w.Name, name));

    public PaneState Spawn(string workspace, string cwd, IReadOnlyList<string> args)
    {
        var pane = NewPane(cwd, args);
        AddTab(WorkspaceOrNew(workspace), pane);
        return pane;
    }

    public PaneState? Split(
        string source, bool sideBySide, bool newFirst, int percent, string cwd, IReadOnlyList<string> args)
    {
        if (!_panes.TryGetValue(source, out var anchor))
        {
            return null;
        }

        var pane = NewPane(cwd, args);
        Join(anchor, pane, sideBySide, newFirst, percent);
        return pane;
    }

    public bool JoinBeside(string source, string moving, bool sideBySide, bool newFirst, int percent)
    {
        if (!_panes.TryGetValue(source, out var anchor)
            || !_panes.TryGetValue(moving, out var pane)
            || source == moving)
        {
            return false;
        }

        Detach(pane);
        Join(anchor, pane, sideBySide, newFirst, percent);
        return true;
    }

    public bool Move(string id, string workspace)
    {
        if (!_panes.TryGetValue(id, out var pane))
        {
            return false;
        }

        Detach(pane);
        AddTab(WorkspaceOrNew(workspace), pane);
        return true;
    }

    public bool Kill(string id)
    {
        if (!_panes.TryGetValue(id, out var pane))
        {
            return false;
        }

        Detach(pane);
        _panes.Remove(id);
        return true;
    }

    public IReadOnlyList<string> PanesIn(string workspace) =>
        Workspace(workspace)?.Tabs.SelectMany(t => t.Root.Panes()).ToList() ?? [];

    public bool Focus(string id)
    {
        if (!_panes.TryGetValue(id, out var pane) || TabOf(pane) is not var (workspace, tab))
        {
            return false;
        }

        workspace.ActiveTab = tab.Id;
        tab.ActivePane = pane.Id;
        return true;
    }

    public bool SetTitle(string id, string title)
    {
        if (!_panes.TryGetValue(id, out var pane) || TabOf(pane) is not var (_, tab))
        {
            return false;
        }

        tab.Title = title;
        return true;
    }

    public ClientState Connect(int cols, int rows, string? showing)
    {
        var client = new ClientState($"c{++_nextClient}")
        {
            Cols = Math.Max(cols, 2),
            Rows = Math.Max(rows, 2),
            LastActive = ++_clock,
        };

        client.Showing = showing is not null && Workspace(showing) is not null
            ? Workspace(showing)!.Name
            : _workspaces.FirstOrDefault(w => !FleetWorkspaces.IsHidden(w.Name))?.Name;

        _clients[client.Id] = client;
        return client;
    }

    public void Disconnect(string client) => _clients.Remove(client);

    public void Resize(string client, int cols, int rows)
    {
        if (_clients.TryGetValue(client, out var c))
        {
            c.Cols = Math.Max(cols, 2);
            c.Rows = Math.Max(rows, 2);
            c.LastActive = ++_clock;
        }
    }

    public void Touch(string client)
    {
        if (_clients.TryGetValue(client, out var c))
        {
            c.LastActive = ++_clock;
        }
    }

    public bool Show(string client, string workspace)
    {
        if (!_clients.TryGetValue(client, out var c) || Workspace(workspace) is not { } target)
        {
            return false;
        }

        c.Showing = target.Name;
        c.LastActive = ++_clock;
        return true;
    }

    public bool CycleTab(string client, int delta)
    {
        if (!_clients.TryGetValue(client, out var c)
            || c.Showing is null
            || Workspace(c.Showing) is not { Tabs.Count: > 0 } workspace)
        {
            return false;
        }

        var index = workspace.Tabs.FindIndex(t => t.Id == workspace.ActiveTab);
        var next = ((index + delta) % workspace.Tabs.Count + workspace.Tabs.Count) % workspace.Tabs.Count;
        workspace.ActiveTab = workspace.Tabs[next].Id;
        return true;
    }

    public bool FocusDirection(string client, int dx, int dy)
    {
        if (View(client) is not { Focused: not null } view)
        {
            return false;
        }

        var from = view.Panes.First(p => p.Pane == view.Focused).Area;
        var cx = from.X + from.Width / 2;
        var cy = from.Y + from.Height / 2;

        var best = view.Panes
            .Where(p => p.Pane != view.Focused)
            .Where(p => dx > 0 ? p.Area.X >= from.X + from.Width
                : dx < 0 ? p.Area.X + p.Area.Width <= from.X
                : dy > 0 ? p.Area.Y >= from.Y + from.Height
                : p.Area.Y + p.Area.Height <= from.Y)
            .OrderBy(p => Math.Abs(p.Area.X + p.Area.Width / 2 - cx) + Math.Abs(p.Area.Y + p.Area.Height / 2 - cy))
            .FirstOrDefault();

        return best.Pane is not null && Focus(best.Pane);
    }

    public IReadOnlyList<Workspace> ListWorkspaces(string? client)
    {
        var showing = client is not null ? Client(client)?.Showing : null;

        return _workspaces
            .Select(w => new Workspace(w.Name, Same(w.Name, showing)))
            .ToList();
    }

    public IReadOnlyList<Pane> ListPanes() =>
        _workspaces
            .SelectMany(w => w.Tabs.SelectMany(t => t.Root.Panes().Select(id => (w, t, id))))
            .Select(x =>
            {
                var pane = _panes[x.id];
                return new Pane(
                    new PaneId(pane.Id),
                    x.w.Name,
                    x.t.Id,
                    x.w.Name,
                    x.t.Title.Length > 0 ? x.t.Title : pane.Title,
                    pane.Cwd,
                    x.w.ActiveTab == x.t.Id && x.t.ActivePane == pane.Id,
                    pane.Title);
            })
            .ToList();

    public ClientView? View(string client)
    {
        if (!_clients.TryGetValue(client, out var c))
        {
            return null;
        }

        var workspace = c.Showing is null ? null : Workspace(c.Showing);
        var tab = workspace?.Tabs.FirstOrDefault(t => t.Id == workspace.ActiveTab);
        var area = new Rect(0, 0, c.Cols, Math.Max(1, c.Rows - StatusRows));
        var placed = new List<Placed>();
        var dividers = new List<Divider>();
        tab?.Root.Place(area, placed, dividers);

        return new ClientView(
            c,
            workspace,
            tab,
            placed,
            dividers,
            tab is not null && tab.Root.Contains(tab.ActivePane) ? tab.ActivePane : null);
    }

    public IReadOnlyList<(PaneState Pane, int Cols, int Rows)> Resizes()
    {
        var changed = new List<(PaneState, int, int)>();

        foreach (var workspace in _workspaces)
        {
            var shownBy = _clients.Values
                .Where(c => Same(c.Showing, workspace.Name))
                .OrderByDescending(c => c.LastActive)
                .FirstOrDefault();

            foreach (var tab in workspace.Tabs)
            {
                var unsized = tab.Root.Panes().Any(id => _panes[id].Cols == 0);

                if (shownBy is null && !unsized)
                {
                    continue;
                }

                var size = shownBy is not null
                    ? (shownBy.Cols, shownBy.Rows)
                    : Reference();
                var placed = new List<Placed>();
                tab.Root.Place(
                    new Rect(0, 0, size.Item1, Math.Max(1, size.Item2 - StatusRows)), placed, []);

                foreach (var p in placed)
                {
                    var pane = _panes[p.Pane];
                    var cols = Math.Max(1, p.Area.Width);
                    var rows = Math.Max(1, p.Area.Height);

                    if (pane.Cols != cols || pane.Rows != rows)
                    {
                        pane.Cols = cols;
                        pane.Rows = rows;
                        changed.Add((pane, cols, rows));
                    }
                }
            }
        }

        return changed;
    }

    private (int Cols, int Rows) Reference() =>
        _clients.Values.OrderByDescending(c => c.LastActive).FirstOrDefault() is { } latest
            ? (latest.Cols, latest.Rows)
            : DefaultSize;

    private PaneState NewPane(string cwd, IReadOnlyList<string> args)
    {
        var pane = new PaneState($"p{++_nextPane}", cwd, args);
        _panes[pane.Id] = pane;
        return pane;
    }

    private WorkspaceState WorkspaceOrNew(string name)
    {
        if (Workspace(name) is { } found)
        {
            return found;
        }

        var created = new WorkspaceState(name);
        _workspaces.Add(created);
        return created;
    }

    private void AddTab(WorkspaceState workspace, PaneState pane)
    {
        var tab = new TabState($"t{++_nextTab}", Layout.Of(pane.Id)) { ActivePane = pane.Id };
        workspace.Tabs.Add(tab);
        workspace.ActiveTab = tab.Id;
    }

    private void Join(PaneState anchor, PaneState pane, bool sideBySide, bool newFirst, int percent)
    {
        if (TabOf(anchor) is not var (_, tab))
        {
            return;
        }

        tab.Root = tab.Root.Split(anchor.Id, pane.Id, sideBySide, newFirst, percent);
        tab.ActivePane = pane.Id;
        pane.Cols = 0;
    }

    private void Detach(PaneState pane)
    {
        if (TabOf(pane) is not var (workspace, tab))
        {
            return;
        }

        var rest = tab.Root.Remove(pane.Id);

        if (rest is not null)
        {
            tab.Root = rest;
            if (tab.ActivePane == pane.Id)
            {
                tab.ActivePane = rest.Panes().First();
            }

            return;
        }

        var index = workspace.Tabs.IndexOf(tab);
        workspace.Tabs.Remove(tab);

        if (workspace.Tabs.Count > 0)
        {
            if (workspace.ActiveTab == tab.Id)
            {
                workspace.ActiveTab = workspace.Tabs[Math.Min(index, workspace.Tabs.Count - 1)].Id;
            }

            return;
        }

        _workspaces.Remove(workspace);

        foreach (var client in _clients.Values.Where(c => Same(c.Showing, workspace.Name)))
        {
            client.Showing = null;
        }
    }

    private (WorkspaceState Workspace, TabState Tab)? TabOf(PaneState pane)
    {
        foreach (var workspace in _workspaces)
        {
            foreach (var tab in workspace.Tabs)
            {
                if (tab.Root.Contains(pane.Id))
                {
                    return (workspace, tab);
                }
            }
        }

        return null;
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed class PaneState(string id, string cwd, IReadOnlyList<string> args)
{
    public string Id { get; } = id;

    public string Cwd { get; set; } = cwd;

    public IReadOnlyList<string> Args { get; } = args;

    public string Title { get; set; } = string.Empty;

    public int Cols { get; set; }

    public int Rows { get; set; }
}

public sealed class TabState(string id, Layout root)
{
    public string Id { get; } = id;

    public Layout Root { get; set; } = root;

    public string Title { get; set; } = string.Empty;

    public string ActivePane { get; set; } = string.Empty;
}

public sealed class WorkspaceState(string name)
{
    public string Name { get; } = name;

    public List<TabState> Tabs { get; } = [];

    public string ActiveTab { get; set; } = string.Empty;
}

public sealed class ClientState(string id)
{
    public string Id { get; } = id;

    public string? Showing { get; set; }

    public int Cols { get; set; }

    public int Rows { get; set; }

    public long LastActive { get; set; }
}

public sealed record ClientView(
    ClientState Client,
    WorkspaceState? Workspace,
    TabState? Tab,
    IReadOnlyList<Placed> Panes,
    IReadOnlyList<Divider> Dividers,
    string? Focused);
