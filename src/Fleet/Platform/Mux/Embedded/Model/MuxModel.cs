using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.Embedded.Model;

public sealed class MuxModel
{
    public const int StatusRows = 1;

    public const string OverlayWorkspace = "fleet~overlay";

    public const string FloatTab = "float";

    public const int MinFloatWidth = 10;

    public const int MinFloatHeight = 4;

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

    public PaneState SpawnFloat(
        string workspace, string cwd, IReadOnlyList<string> args, Rect? bounds = null, bool modal = false)
    {
        var pane = NewPane(cwd, args);
        AddFloat(WorkspaceOrNew(workspace), pane, bounds, modal);
        return pane;
    }

    public bool ToggleFloats(string client)
    {
        if (View(client)?.Workspace is not { } workspace || workspace.Floats.All(f => f.Modal))
        {
            return false;
        }

        workspace.FloatsShown = !workspace.FloatsShown;
        workspace.FloatFocused = workspace.FloatsShown || workspace.Floats.Any(f => f.Modal);
        return true;
    }

    public Rect? PaneArea(string pane)
    {
        if (!_panes.TryGetValue(pane, out var state) || TabOf(state) is not var (workspace, tab))
        {
            return null;
        }

        var (cols, rows) = _clients.Values
            .Where(c => Same(c.Showing, workspace.Name))
            .OrderByDescending(c => c.LastActive)
            .Select(c => (c.Cols, c.Rows))
            .DefaultIfEmpty(Reference())
            .First();

        var placed = new List<Placed>();
        tab.Root.Place(Content(cols, rows), placed, []);
        return placed.FirstOrDefault(p => p.Pane == pane).Area;
    }

    public bool MoveFloat(string pane, int x, int y)
    {
        if (FloatOf(pane) is not var (_, box))
        {
            return false;
        }

        box.Bounds = box.Bounds with { X = Math.Max(0, x), Y = Math.Max(StatusRows, y) };
        return true;
    }

    public bool ResizeFloat(string pane, int width, int height)
    {
        if (FloatOf(pane) is not var (_, box))
        {
            return false;
        }

        box.Bounds = box.Bounds with { Width = Math.Max(MinFloatWidth, width), Height = Math.Max(MinFloatHeight, height) };
        return true;
    }

    public bool NudgeFloat(string client, int dx, int dy, int dw, int dh)
    {
        if (View(client) is not { Focused: { } focused } view
            || view.FloatingPanes.FirstOrDefault(p => p.Pane == focused) is not { Pane: not null } box)
        {
            return false;
        }

        var area = box.Area;
        return ResizeFloat(focused, area.Width + dw, area.Height + dh)
            && MoveFloat(focused, area.X + dx, area.Y + dy);
    }

    public bool ToFloat(string id)
    {
        if (!_panes.TryGetValue(id, out var pane)
            || TabOf(pane) is not var (workspace, _)
            || Same(workspace.Name, OverlayWorkspace))
        {
            return false;
        }

        AddFloat(workspace, pane);
        Detach(pane);
        return true;
    }

    public bool ToTile(string id)
    {
        if (!_panes.TryGetValue(id, out var pane) || FloatOf(id) is not var (workspace, box) || box.Modal)
        {
            return false;
        }

        if (workspace.Tabs.FirstOrDefault(t => t.Id == workspace.ActiveTab) is { } tab
            && _panes.TryGetValue(tab.ActivePane, out var anchor))
        {
            Join(anchor, pane, sideBySide: true, newFirst: false, 50);
        }
        else
        {
            AddTab(workspace, pane);
        }

        RemoveFloat(workspace, box);
        workspace.FloatFocused = false;
        pane.Cols = 0;
        return true;
    }

    public Rect? FloatBounds(string pane) => FloatOf(pane)?.Float.Bounds;

    public PaneState? Split(
        string source, bool sideBySide, bool newFirst, int percent, string cwd, IReadOnlyList<string> args)
    {
        if (!_panes.TryGetValue(source, out var anchor) || TabOf(anchor) is null)
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

        foreach (var client in _clients.Values.Where(c => c.Overlay == id))
        {
            client.Overlay = null;
        }

        return true;
    }

    public IReadOnlyList<string> PanesIn(string workspace) =>
        Workspace(workspace) is { } found
            ? found.Tabs.SelectMany(t => t.Root.Panes()).Concat(found.Floats.Select(f => f.Pane)).ToList()
            : [];

    public bool Focus(string id)
    {
        if (!_panes.TryGetValue(id, out var pane))
        {
            return false;
        }

        if (FloatOf(id) is var (floatWorkspace, box))
        {
            floatWorkspace.Floats.Remove(box);
            floatWorkspace.Floats.Add(box);
            floatWorkspace.FloatsShown = true;
            floatWorkspace.FloatFocused = true;
            return true;
        }

        if (TabOf(pane) is not var (workspace, tab))
        {
            return false;
        }

        workspace.ActiveTab = tab.Id;
        tab.ActivePane = pane.Id;
        workspace.FloatFocused = false;
        return true;
    }

    public bool SetTitle(string id, string title)
    {
        if (FloatOf(id) is var (_, box))
        {
            box.Title = title;
            return true;
        }

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
            : _workspaces.FirstOrDefault(w => !FleetWorkspaces.IsHidden(w.Name) && !Same(w.Name, OverlayWorkspace))?.Name;

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

    public string? WindowTitle(string client)
    {
        if (View(client) is not { } view)
        {
            return null;
        }

        if (view.Workspace is not { } workspace)
        {
            return "fleet";
        }

        var title = view.Focused is { } focused ? Pane(focused)?.Title : null;

        return string.IsNullOrWhiteSpace(title) ? $"{workspace.Name} · fleet" : $"{title} · {workspace.Name}";
    }

    public MouseHit Hit(string client, int x, int y)
    {
        if (View(client) is not { } view)
        {
            return MouseHit.Nothing;
        }

        if (view.Overlay is { } overlay)
        {
            var inner = new Rect(overlay.Area.X + 1, overlay.Area.Y + 1, overlay.Area.Width - 2, overlay.Area.Height - 2);
            return inner.Contains(x, y)
                ? new MouseHit(MouseHitKind.Pane, overlay.Pane, x - inner.X, y - inner.Y)
                : MouseHit.Nothing;
        }

        if (y < StatusRows)
        {
            return new MouseHit(MouseHitKind.StatusBar, null, x, y);
        }

        for (var i = view.FloatingPanes.Count - 1; i >= 0; i--)
        {
            var box = view.FloatingPanes[i];
            if (!box.Area.Contains(x, y))
            {
                continue;
            }

            var inner = ClientView.Inner(box.Area);
            if (inner.Contains(x, y))
            {
                return new MouseHit(MouseHitKind.Pane, box.Pane, x - inner.X, y - inner.Y);
            }

            var corner = x == box.Area.X + box.Area.Width - 1 && y == box.Area.Y + box.Area.Height - 1;
            return new MouseHit(corner ? MouseHitKind.FloatResize : MouseHitKind.FloatMove, box.Pane, x, y);
        }

        for (var i = 0; i < view.Dividers.Count; i++)
        {
            if (view.Dividers[i].Contains(x, y))
            {
                return new MouseHit(MouseHitKind.Divider, null, x, y, i);
            }
        }

        foreach (var placed in view.Panes)
        {
            if (placed.Area.Contains(x, y))
            {
                return new MouseHit(MouseHitKind.Pane, placed.Pane, x - placed.Area.X, y - placed.Area.Y);
            }
        }

        return MouseHit.Nothing;
    }

    public (int X, int Y)? Relative(string client, string pane, int x, int y)
    {
        if (View(client) is not { } view)
        {
            return null;
        }

        var area = view.Overlay is { } overlay && overlay.Pane == pane
            ? new Rect(overlay.Area.X + 1, overlay.Area.Y + 1, overlay.Area.Width - 2, overlay.Area.Height - 2)
            : view.FloatingPanes.Any(p => p.Pane == pane)
                ? ClientView.Inner(view.FloatingPanes.First(p => p.Pane == pane).Area)
                : view.Panes.FirstOrDefault(p => p.Pane == pane).Area;

        if (area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        return (Math.Clamp(x - area.X, 0, area.Width - 1), Math.Clamp(y - area.Y, 0, area.Height - 1));
    }

    public bool FocusTab(string client, string tab)
    {
        if (View(client)?.Workspace is not { } workspace || workspace.Tabs.All(t => t.Id != tab))
        {
            return false;
        }

        workspace.ActiveTab = tab;
        return true;
    }

    public bool DragDivider(string client, int index, int x, int y)
    {
        if (View(client) is not { Tab: { } tab } view || index < 0 || index >= view.Dividers.Count)
        {
            return false;
        }

        var divider = view.Dividers[index];
        if (divider.Split is not { } split)
        {
            return false;
        }

        var area = divider.Area;
        var usable = Math.Max(2, (divider.Vertical ? area.Width : area.Height) - 1);
        var offset = divider.Vertical ? x - area.X : y - area.Y;
        tab.Root = tab.Root.WithRatio(split, (double)Math.Clamp(offset, 1, usable - 1) / usable);
        return true;
    }

    public bool FocusDirection(string client, int dx, int dy)
    {
        if (View(client) is not { Focused: not null } view || view.Panes.All(p => p.Pane != view.Focused))
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

    public bool SetOverlay(string client, string pane)
    {
        if (!_clients.TryGetValue(client, out var c) || !_panes.ContainsKey(pane))
        {
            return false;
        }

        c.Overlay = pane;
        return true;
    }

    public static Rect OverlayArea(int cols, int rows)
    {
        var usable = Math.Max(1, rows - StatusRows);
        var width = Math.Clamp(cols * 4 / 5, Math.Min(cols, 40), cols);
        var height = Math.Clamp(usable * 4 / 5, Math.Min(usable, 12), usable);
        return new Rect((cols - width) / 2, StatusRows + (usable - height) / 2, width, height);
    }

    public static Rect Content(int cols, int rows) => new(0, StatusRows, cols, Math.Max(1, rows - StatusRows));

    public IReadOnlyList<Workspace> ListWorkspaces(string? client)
    {
        var showing = client is not null ? Client(client)?.Showing : null;

        return _workspaces
            .Where(w => !Same(w.Name, OverlayWorkspace))
            .Select(w => new Workspace(w.Name, Same(w.Name, showing)))
            .ToList();
    }

    public IReadOnlyList<Pane> ListPanes() =>
        _workspaces
            .Where(w => !Same(w.Name, OverlayWorkspace))
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
            .Concat(_workspaces
                .Where(w => !Same(w.Name, OverlayWorkspace))
                .SelectMany(w => w.Floats.Select(f =>
                {
                    var pane = _panes[f.Pane];
                    return new Pane(
                        new PaneId(pane.Id),
                        w.Name,
                        FloatTab,
                        w.Name,
                        f.Title.Length > 0 ? f.Title : pane.Title,
                        pane.Cwd,
                        w.FloatFocused && w.Floats[^1] == f,
                        pane.Title);
                })))
            .ToList();

    public ClientView? View(string client)
    {
        if (!_clients.TryGetValue(client, out var c))
        {
            return null;
        }

        var workspace = c.Showing is null ? null : Workspace(c.Showing);
        var tab = workspace?.Tabs.FirstOrDefault(t => t.Id == workspace.ActiveTab);
        var area = Content(c.Cols, c.Rows);
        var placed = new List<Placed>();
        var dividers = new List<Divider>();
        tab?.Root.Place(area, placed, dividers);

        var overlay = c.Overlay is { } o && _panes.ContainsKey(o)
            ? new Placed(o, OverlayArea(c.Cols, c.Rows))
            : (Placed?)null;

        var floats = workspace is not null
            ? workspace.Floats
                .Where(f => workspace.FloatsShown || f.Modal)
                .Select(f => new Placed(f.Pane, FloatArea(f.Bounds, area)))
                .ToList()
            : [];

        var focused = overlay?.Pane
            ?? (floats.Count > 0 && (workspace!.FloatFocused || tab is null) ? floats[^1].Pane : null)
            ?? (tab is not null && tab.Root.Contains(tab.ActivePane) ? tab.ActivePane : null);

        var labels = workspace?.Floats.ToDictionary(f => f.Pane, f => f.Title.Length > 0 ? f.Title : _panes[f.Pane].Title);

        return new ClientView(c, workspace, tab, placed, dividers, focused, overlay, floats, labels);
    }

    public static Rect Over(Rect pane)
    {
        var width = Math.Clamp(pane.Width, 50, 72);
        var height = Math.Clamp(pane.Height, 10, 14);
        return new Rect(
            Math.Max(0, pane.X + (pane.Width - width) / 2),
            Math.Max(0, pane.Y + (pane.Height - height) / 2),
            width,
            height);
    }

    public static Rect FloatArea(Rect bounds, Rect content)
    {
        var width = Math.Min(bounds.Width, content.Width);
        var height = Math.Min(bounds.Height, content.Height);
        return new Rect(
            Math.Clamp(bounds.X, content.X, content.X + content.Width - width),
            Math.Clamp(bounds.Y, content.Y, content.Y + content.Height - height),
            width,
            height);
    }

    public IReadOnlyList<(PaneState Pane, int Cols, int Rows)> Resizes()
    {
        var changed = new List<(PaneState, int, int)>();

        foreach (var workspace in _workspaces.Where(w => !Same(w.Name, OverlayWorkspace)))
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
                    Content(size.Item1, size.Item2), placed, []);

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

            foreach (var box in workspace.Floats)
            {
                var pane = _panes[box.Pane];

                if (shownBy is null && pane.Cols != 0)
                {
                    continue;
                }

                var size = shownBy is not null ? (shownBy.Cols, shownBy.Rows) : Reference();
                var inner = ClientView.Inner(
                    FloatArea(box.Bounds, Content(size.Item1, size.Item2)));
                var cols = Math.Max(1, inner.Width);
                var rows = Math.Max(1, inner.Height);

                if (pane.Cols != cols || pane.Rows != rows)
                {
                    pane.Cols = cols;
                    pane.Rows = rows;
                    changed.Add((pane, cols, rows));
                }
            }
        }

        foreach (var client in _clients.Values)
        {
            if (client.Overlay is { } id && _panes.TryGetValue(id, out var pane))
            {
                var area = OverlayArea(client.Cols, client.Rows);
                var cols = Math.Max(1, area.Width - 2);
                var rows = Math.Max(1, area.Height - 2);

                if (pane.Cols != cols || pane.Rows != rows)
                {
                    pane.Cols = cols;
                    pane.Rows = rows;
                    changed.Add((pane, cols, rows));
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

    private void AddFloat(WorkspaceState workspace, PaneState pane, Rect? bounds = null, bool modal = false)
    {
        var (cols, rows) = Reference();
        var usable = Math.Max(1, rows - StatusRows);
        var width = Math.Max(MinFloatWidth, cols * 3 / 5);
        var height = Math.Max(MinFloatHeight, usable * 3 / 5);
        var cascade = workspace.Floats.Count * 2;

        workspace.Floats.Add(new FloatState(
            pane.Id,
            bounds ?? new Rect((cols - width) / 2 + cascade, StatusRows + (usable - height) / 2 + cascade / 2, width, height))
        {
            Modal = modal,
        });

        workspace.FloatsShown |= !modal;
        workspace.FloatFocused = true;
        pane.Cols = 0;
    }

    private void RemoveFloat(WorkspaceState workspace, FloatState box)
    {
        workspace.Floats.Remove(box);

        if (workspace.Floats.All(f => f.Modal))
        {
            workspace.FloatsShown = false;
        }

        if (!workspace.Floats.Any(f => workspace.FloatsShown || f.Modal))
        {
            workspace.FloatFocused = false;
        }

        if (workspace.Tabs.Count == 0 && workspace.Floats.Count == 0)
        {
            DropWorkspace(workspace);
        }
    }

    private void DropWorkspace(WorkspaceState workspace)
    {
        _workspaces.Remove(workspace);

        foreach (var client in _clients.Values.Where(c => Same(c.Showing, workspace.Name)))
        {
            client.Showing = null;
        }
    }

    private (WorkspaceState Workspace, FloatState Float)? FloatOf(string pane)
    {
        foreach (var workspace in _workspaces)
        {
            if (workspace.Floats.FirstOrDefault(f => f.Pane == pane) is { } box)
            {
                return (workspace, box);
            }
        }

        return null;
    }

    private void Detach(PaneState pane)
    {
        if (TabOf(pane) is not var (workspace, tab))
        {
            if (FloatOf(pane.Id) is var (floatWorkspace, box))
            {
                RemoveFloat(floatWorkspace, box);
            }

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

        if (workspace.Floats.Count == 0)
        {
            DropWorkspace(workspace);
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

    public List<FloatState> Floats { get; } = [];

    public bool FloatsShown { get; set; }

    public bool FloatFocused { get; set; }
}

public sealed class FloatState(string pane, Rect bounds)
{
    public string Pane { get; } = pane;

    public Rect Bounds { get; set; } = bounds;

    public string Title { get; set; } = string.Empty;

    public bool Modal { get; init; }
}

public sealed class ClientState(string id)
{
    public string Id { get; } = id;

    public string? Showing { get; set; }

    public int Cols { get; set; }

    public int Rows { get; set; }

    public long LastActive { get; set; }

    public string? Overlay { get; set; }

    public string? Menu { get; set; }
}

public enum MouseHitKind
{
    None,
    Pane,
    Divider,
    StatusBar,
    FloatMove,
    FloatResize,
}

public sealed record MouseHit(MouseHitKind Kind, string? Pane, int X, int Y, int Divider = -1)
{
    public static readonly MouseHit Nothing = new(MouseHitKind.None, null, 0, 0);
}

public sealed record ClientView(
    ClientState Client,
    WorkspaceState? Workspace,
    TabState? Tab,
    IReadOnlyList<Placed> Panes,
    IReadOnlyList<Divider> Dividers,
    string? Focused,
    Placed? Overlay = null,
    IReadOnlyList<Placed>? Floats = null,
    IReadOnlyDictionary<string, string>? FloatLabels = null)
{
    public IReadOnlyList<Placed> FloatingPanes => Floats ?? [];

    public string? FloatLabel(string pane) => FloatLabels?.GetValueOrDefault(pane);

    public static Rect Inner(Rect box) => new(box.X + 1, box.Y + 1, Math.Max(0, box.Width - 2), Math.Max(0, box.Height - 2));
}
