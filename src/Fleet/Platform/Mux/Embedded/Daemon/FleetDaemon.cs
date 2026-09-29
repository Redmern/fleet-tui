using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Platform.Mux.Embedded.Pty;
using Fleet.Platform.Mux.Embedded.Render;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public sealed class DaemonOptions
{
    public required Endpoint Endpoint { get; init; }

    public required Func<IPanePty> Pty { get; init; }

    public required PaneTerminalFactory Terminal { get; init; }

    public Action<string> Log { get; init; } = _ => { };

    public string FleetExecutable { get; init; } = "fleet";

    public TimeSpan FrameDelay { get; init; } = TimeSpan.FromMilliseconds(4);

    public TimeSpan? ExitWhenEmptyAfter { get; init; }

    public string? SessionFile { get; init; }

    public TimeSpan SaveEvery { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan RevealWhenQuiet { get; init; } = TimeSpan.FromMilliseconds(60);

    public bool WarmMenus { get; init; }

    public Func<string, IReadOnlyDictionary<string, string>?> PaneEnv { get; init; } = _ => null;

    public Func<IReadOnlyList<string>> Shell { get; init; } = () => [FleetDaemon.DefaultShell()];
}

public sealed class FleetDaemon(DaemonOptions options)
{
    public const string ClientVariable = "FLEET_CLIENT";
    public const string PaneVariable = "FLEET_PANE";

    private readonly Lock _gate = new();
    private readonly MuxModel _model = new();
    private readonly Dictionary<string, PaneRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AttachSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Pane, string Text)> _copies = new();
    private readonly CancellationTokenSource _stop = new();
    private DateTime _lastBusy = DateTime.UtcNow;
    private DateTime _lastSave = DateTime.MinValue;
    private string? _savedSession;

    public MuxModel Model => _model;

    public void Stop() => _stop.Cancel();

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        var token = linked.Token;

        using var listener = options.Endpoint.Listen();
        options.Log($"fleetd listening on {options.Endpoint.Address}");

        RestoreSession();

        var render = Task.Run(() => RenderLoopAsync(token), CancellationToken.None);

        try
        {
            while (!token.IsCancellationRequested)
            {
                var stream = await listener.AcceptAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => ServeAsync(stream, token), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _stop.Cancel();
            await render.ConfigureAwait(false);
            Shutdown();
        }
    }

    public ControlResponse Execute(ControlRequest request, string? attachedClient = null)
    {
        var response = new ControlResponse { Id = request.Id, Ok = true };

        try
        {
            lock (_gate)
            {
                switch (request.Op)
                {
                    case "ping":
                        break;
                    case "status":
                        response.Status = new DaemonStatusDto
                        {
                            Pid = Environment.ProcessId,
                            Executable = Environment.ProcessPath ?? string.Empty,
                            Workspaces = _model.ListWorkspaces(null).Count,
                            Panes = _model.Panes.Count(p => _model.Float(p.Id) is not { Parked: true }),
                            WarmMenus = _model.Panes.Count(p => _model.Float(p.Id) is { Parked: true }),
                            Clients = _sessions.Count,
                            SessionFile = options.SessionFile,
                        };
                        break;
                    case "list-panes":
                        response.Panes = _model.ListPanes().Select(p => new PaneDto
                        {
                            Id = p.Id.Value,
                            Window = p.WindowId,
                            Tab = p.TabId,
                            Session = p.SessionName,
                            Title = p.Title,
                            Cwd = p.Cwd,
                            Active = p.IsActive,
                            PaneTitle = p.PaneTitle,
                        }).ToList();
                        break;
                    case "list-workspaces":
                        response.Workspaces = _model.ListWorkspaces(ClientFor(request, attachedClient))
                            .Select(w => new WorkspaceDto
                            {
                                Name = w.Name,
                                ShownHere = w.ShownHere,
                                InWindow = w.InWindow,
                                InOtherWindow = w.InOtherWindow,
                            })
                            .ToList();
                        break;
                    case "spawn":
                        response.Pane = Spawn(request);
                        break;
                    case "spawn-float":
                        response.Pane = SpawnFloat(request);
                        break;
                    case "menu":
                        {
                            var caller = request.Pane ?? request.Caller ?? string.Empty;
                            var home = CallerWorkspace(caller) ?? throw new InvalidOperationException($"no pane {caller}");
                            var client = _model.Clients
                                .Where(c => string.Equals(c.Showing, home, StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(c => c.LastActive)
                                .FirstOrDefault()
                                ?? throw new InvalidOperationException($"no client shows {home}");
                            OpenMenu(client.Id, request.Text);
                            break;
                        }

                    case "fit":
                        Fit(request.Pane ?? request.Caller ?? string.Empty, request.Cols, request.Rows, request);
                        break;
                    case "hold":
                        Hold(request.Pane ?? request.Caller ?? string.Empty);
                        break;
                    case "focus-from":
                        {
                            var (dx, dy) = Direction(request.Direction);
                            _model.FocusDirectionFrom(request.Pane ?? request.Caller ?? string.Empty, dx, dy);
                            break;
                        }
                    case "split":
                        response.Pane = Split(request);
                        break;
                    case "kill":
                        Require(Kill(request.Pane), request);
                        break;
                    case "move":
                        Require(_model.Move(Pane(request), MoveTarget(request)), request);
                        break;
                    case "title":
                        Require(_model.SetTitle(Pane(request), request.Text ?? string.Empty), request);
                        break;
                    case "focus":
                        Require(_model.Focus(Pane(request)), request);
                        break;
                    case "send-text":
                        Runtime(request).Send(Encoding.UTF8.GetBytes(request.Text ?? string.Empty));
                        break;
                    case "get-text":
                        {
                            var runtime = Runtime(request);
                            lock (runtime.Gate)
                            {
                                response.Text = runtime.Terminal.PlainText();
                            }

                            break;
                        }

                    case "show":
                        response.Ms = Show(ClientFor(request, attachedClient), request.Workspace);
                        break;
                    case "open-window":
                        OpenWindow(ClientFor(request, attachedClient), request.Workspace);
                        break;
                    case "close-workspace":
                        foreach (var id in _model.PanesIn(request.Workspace ?? string.Empty))
                        {
                            Kill(id);
                        }

                        break;
                    case "shutdown":
                        _stop.Cancel();
                        break;
                    default:
                        throw new InvalidOperationException($"unknown operation '{request.Op}'");
                }

                ApplyResizes();

                if (request.Op == "fit" && _model.Pane(request.Pane ?? request.Caller ?? string.Empty) is { } fitted)
                {
                    (response.Cols, response.Rows) = (fitted.Cols, fitted.Rows);
                }
            }
        }
        catch (InvalidOperationException e)
        {
            response.Ok = false;
            response.Error = e.Message;
        }

        _wake.Release();
        return response;
    }

    private async Task ServeAsync(Stream stream, CancellationToken ct)
    {
        using var wire = new Wire(stream);
        AttachSession? session = null;

        try
        {
            if (await wire.ReceiveAsync(ct).ConfigureAwait(false) is not { Type: MessageType.Hello } first)
            {
                return;
            }

            var hello = Wire.Read(first.Payload, WireJsonContext.Default.Hello);
            if (hello.Version != Wire.Version)
            {
                await wire.SendAsync(
                    MessageType.Error,
                    new ErrorMessage { Message = $"fleetd speaks protocol {Wire.Version}, this client speaks {hello.Version}" },
                    WireJsonContext.Default.ErrorMessage,
                    ct).ConfigureAwait(false);
                return;
            }

            var clientId = string.Empty;
            if (hello.Role == ClientRoles.Attach)
            {
                lock (_gate)
                {
                    var client = _model.Connect(hello.Cols, hello.Rows, hello.Workspace);
                    clientId = client.Id;
                    session = new AttachSession(client.Id, wire);
                    _sessions[client.Id] = session;
                    ApplyResizes();
                }

                options.Log($"client {clientId} attached ({hello.Os}, {hello.Cols}x{hello.Rows})");
            }

            await wire.SendAsync(
                MessageType.Welcome,
                new Welcome { Version = Wire.Version, Client = clientId },
                WireJsonContext.Default.Welcome,
                ct).ConfigureAwait(false);

            _wake.Release();

            while (!ct.IsCancellationRequested)
            {
                if (await wire.ReceiveAsync(ct).ConfigureAwait(false) is not { } message
                    || message.Type == MessageType.Bye)
                {
                    break;
                }

                await HandleAsync(wire, session, message.Type, message.Payload, ct).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or InvalidDataException
                                      or OperationCanceledException or System.Text.Json.JsonException
                                      or ObjectDisposedException)
        {
            options.Log($"connection closed: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            if (session is not null)
            {
                Detach(session);
            }
        }
    }

    private async Task HandleAsync(
        Wire wire, AttachSession? session, MessageType type, byte[] payload, CancellationToken ct)
    {
        switch (type)
        {
            case MessageType.Request:
                {
                    var request = Wire.Read(payload, WireJsonContext.Default.ControlRequest);
                    var response = Execute(request, session?.Client);
                    await wire.SendAsync(MessageType.Response, response, WireJsonContext.Default.ControlResponse, ct)
                        .ConfigureAwait(false);
                    break;
                }

            case MessageType.Key when session is not null:
                Route(session, Wire.Read(payload, WireJsonContext.Default.KeyMessage));
                break;

            case MessageType.Text when session is not null:
                Route(session, Wire.Read(payload, WireJsonContext.Default.TextMessage));
                break;

            case MessageType.Resize when session is not null:
                {
                    var resize = Wire.Read(payload, WireJsonContext.Default.ResizeMessage);
                    lock (_gate)
                    {
                        _model.Resize(session.Client, resize.Cols, resize.Rows);
                        ApplyResizes();
                    }

                    _wake.Release();
                    break;
                }

            case MessageType.Mouse when session is not null:
                Mouse(session, Wire.Read(payload, WireJsonContext.Default.MouseMessage));
                break;

            case MessageType.Badge when session is not null:
                {
                    var badge = Wire.Read(payload, WireJsonContext.Default.BadgeMessage);
                    session.Badge = badge.Text;
                    session.WhichKey = badge.Text is null ? null : badge.Keys;
                    _wake.Release();
                    break;
                }

            case MessageType.Command when session is not null:
                Command(session, Wire.Read(payload, WireJsonContext.Default.CommandMessage));
                break;
        }
    }

    private void Route(AttachSession session, KeyMessage key)
    {
        PaneRuntime? target;
        lock (_gate)
        {
            _model.Touch(session.Client);
            target = FocusedRuntime(session.Client);
        }

        if (target is null)
        {
            return;
        }

        FollowOutput(target);

        if (target.Modes.Win32Input && key.Win32 is { } w)
        {
            target.Send(Input.ConPtyModes.Encode(w.Vk, w.Sc, w.Uc, w.Down, w.State, w.Repeat));
            return;
        }

        if (key.Action == 0 || (key.Key == (int)Key.Unidentified && string.IsNullOrEmpty(key.Text)))
        {
            return;
        }

        byte[] bytes;
        lock (target.Gate)
        {
            bytes = target.Terminal.Encode(key);

            if (key.Repeat > 1 && key.Action == (int)KeyAction.Press)
            {
                var again = Repeated(key);
                var more = new List<byte>(bytes);
                for (var i = 1; i < key.Repeat; i++)
                {
                    more.AddRange(target.Terminal.Encode(again));
                }

                bytes = [.. more];
            }
        }

        target.Send(bytes);
    }

    private static KeyMessage Repeated(KeyMessage key) => new()
    {
        Key = key.Key,
        Mods = key.Mods,
        Consumed = key.Consumed,
        Text = key.Text,
        Action = (int)KeyAction.Repeat,
        Unshifted = key.Unshifted,
    };

    private void Route(AttachSession session, TextMessage text)
    {
        PaneRuntime? target;
        lock (_gate)
        {
            _model.Touch(session.Client);
            target = FocusedRuntime(session.Client);
        }

        if (target is null)
        {
            return;
        }

        FollowOutput(target);

        if (text.Bytes is { } raw)
        {
            target.Send(Convert.FromBase64String(raw));
            return;
        }

        target.Send(Encoding.UTF8.GetBytes(PasteBytes(text, target.Modes.BracketedPaste)));
    }

    private void Mouse(AttachSession session, MouseMessage mouse)
    {
        PaneRuntime? target = null;
        var x = 0;
        var y = 0;
        var redraw = false;

        lock (_gate)
        {
            _model.Touch(session.Client);

            var wheel = mouse.Button >= MouseButtons.WheelUp;
            var press = mouse.Action == MouseActions.Press && !wheel;

            if (session.Capture is { } capture && !press)
            {
                if (mouse.Action == MouseActions.Release && !mouse.Held)
                {
                    session.Capture = null;
                }

                if (capture.Kind is MouseHitKind.FloatMove or MouseHitKind.FloatResize)
                {
                    if (mouse.Action == MouseActions.Motion && DragFloat(capture, mouse.X, mouse.Y))
                    {
                        ApplyResizes();
                        redraw = true;
                    }
                }
                else if (capture.Divider >= 0)
                {
                    if (mouse.Action == MouseActions.Motion && _model.DragDivider(session.Client, capture.Divider, mouse.X, mouse.Y))
                    {
                        ApplyResizes();
                        redraw = true;
                    }
                }
                else if (_model.Relative(session.Client, capture.Pane!, mouse.X, mouse.Y) is { } relative)
                {
                    target = _runtimes.GetValueOrDefault(capture.Pane!);
                    (x, y) = relative;
                }
            }
            else
            {
                var hit = _model.Hit(session.Client, mouse.X, mouse.Y);

                switch (hit.Kind)
                {
                    case MouseHitKind.StatusBar when press && mouse.Button == MouseButtons.Left:
                        if (_model.View(session.Client) is { } view
                            && Composer.TabSpans(view).FirstOrDefault(s => mouse.X >= s.Start && mouse.X < s.End) is { Tab: { } tab })
                        {
                            redraw = _model.FocusTab(session.Client, tab);
                        }
                        else if (_model.View(session.Client) is { } bar
                                 && Composer.FloatSpan(bar) is var (start, end, _)
                                 && mouse.X >= start && mouse.X < end)
                        {
                            redraw = _model.ToggleFloats(session.Client);
                        }

                        break;

                    case MouseHitKind.FloatMove or MouseHitKind.FloatResize when press && mouse.Button == MouseButtons.Left:
                        if (_model.View(session.Client)?.FloatingPanes.FirstOrDefault(p => p.Pane == hit.Pane) is { Pane: not null } box)
                        {
                            _model.Focus(hit.Pane!);
                            session.Capture = new MouseCapture(hit.Pane, -1, hit.Kind, box.Area, mouse.X, mouse.Y);
                            redraw = true;
                        }

                        break;

                    case MouseHitKind.Divider when press:
                        session.Capture = new MouseCapture(null, hit.Divider);
                        break;

                    case MouseHitKind.Pane:
                        if (press)
                        {
                            session.Capture = new MouseCapture(hit.Pane, -1);
                            if (_model.View(session.Client)?.Focused != hit.Pane && _model.Focus(hit.Pane!))
                            {
                                redraw = true;
                            }
                        }

                        target = _runtimes.GetValueOrDefault(hit.Pane!);
                        (x, y) = (hit.X, hit.Y);
                        break;
                }
            }
        }

        if (target is not null)
        {
            byte[] bytes;
            lock (target.Gate)
            {
                bytes = target.Terminal.EncodeMouse(mouse, x, y);

                if (bytes.Length == 0 && mouse.Button >= MouseButtons.WheelUp && mouse.Action == MouseActions.Press)
                {
                    bytes = Wheel(target, mouse.Button == MouseButtons.WheelUp);
                    redraw |= bytes.Length == 0;
                }
            }

            target.Send(bytes);
        }

        if (redraw)
        {
            _wake.Release();
        }
    }

    public const int WheelLines = 3;

    private static byte[] Wheel(PaneRuntime pane, bool up)
    {
        if (pane.Terminal.Viewport.AltScreen)
        {
            var arrow = new KeyMessage { Key = (int)(up ? Key.ArrowUp : Key.ArrowDown), Action = (int)KeyAction.Press };
            var once = pane.Terminal.Encode(arrow);
            return [.. once, .. once, .. once];
        }

        pane.Terminal.Scroll(ScrollTo.Delta, up ? -WheelLines : WheelLines);
        pane.Dirty = true;
        return [];
    }

    private void FollowOutput(PaneRuntime pane)
    {
        lock (pane.Gate)
        {
            if (pane.Terminal.Viewport.AtBottom)
            {
                return;
            }

            pane.Terminal.Scroll(ScrollTo.Bottom);
            pane.Dirty = true;
        }

        _wake.Release();
    }

    public static string PasteBytes(TextMessage text, bool bracketed)
    {
        var body = text.Text ?? string.Empty;

        if (!text.Paste || !bracketed)
        {
            return body;
        }

        return $"\e[200~{body.Replace("\e[201~", string.Empty, StringComparison.Ordinal)}\e[201~";
    }

    private void Command(AttachSession session, CommandMessage command)
    {
        if (command.Name is not ("focus-in" or "focus-out" or "copy" or "float-move" or "float-size"))
        {
            options.Log($"{session.Client}: {command.Name}{(command.Arg is null ? string.Empty : " " + command.Arg)}");
        }

        switch (command.Name)
        {
            case "smart-focus":
                SmartFocus(session, command);
                return;
            case "newline":
                Newline(session, command);
                return;
        }

        lock (_gate)
        {
            _model.Touch(session.Client);

            switch (command.Name)
            {
                case "next-tab":
                    _model.CycleTab(session.Client, 1);
                    break;
                case "prev-tab":
                    _model.CycleTab(session.Client, -1);
                    break;
                case "focus-left":
                    _model.FocusDirection(session.Client, -1, 0);
                    break;
                case "focus-right":
                    _model.FocusDirection(session.Client, 1, 0);
                    break;
                case "focus-up":
                    _model.FocusDirection(session.Client, 0, -1);
                    break;
                case "focus-down":
                    _model.FocusDirection(session.Client, 0, 1);
                    break;
                case "next-workspace":
                    NextWorkspace(session.Client);
                    break;
                case "show" when command.Arg is { } workspace:
                    Show(session.Client, workspace);
                    break;
                case "menu":
                    OpenMenu(session.Client, command.Arg);
                    break;
                case "redraw":
                    session.Shown = null;
                    break;
                case "split-right" or "split-down":
                    SplitFocused(session.Client, command.Name == "split-right");
                    break;
                case "new-tab":
                    NewTab(session.Client);
                    break;
                case "tab" when int.TryParse(command.Arg, out var number):
                    _model.FocusTabIndex(session.Client, number - 1);
                    break;
                case "resize":
                    _model.ResizeFocused(session.Client, command.Arg ?? string.Empty, ResizeCells);
                    break;
                case "zoom":
                    _model.ToggleZoom(session.Client);
                    break;
                case "next-pane":
                    _model.NextPane(session.Client);
                    break;
                case "kill-pane":
                    if (_model.View(session.Client)?.Focused is { } doomed)
                    {
                        Kill(doomed);
                    }

                    break;
                case "kill-tab":
                    if (_model.View(session.Client)?.Tab is { } closing)
                    {
                        foreach (var id in closing.Root.Panes().ToList())
                        {
                            Kill(id);
                        }
                    }

                    break;
                case "switch-project":
                    OpenMenu(session.Client, "switch-project");
                    break;
                case "copy-mode":
                    if (FocusedRuntime(session.Client) is { } scrolled)
                    {
                        lock (scrolled.Gate)
                        {
                            scrolled.Terminal.Snapshot(scrolled.Screen);
                            session.Copy = CopySession.Enter(scrolled.Id, scrolled.Terminal, scrolled.Screen);
                            scrolled.Dirty = true;
                        }
                    }

                    break;
                case "copy":
                    CopyStep(session, command.Arg ?? string.Empty);
                    break;
                case "float-new":
                    NewFloat(session.Client);
                    break;
                case "float-toggle":
                    _model.ToggleFloats(session.Client);
                    break;
                case "float-move" or "float-size":
                    {
                        var (dx, dy) = command.Arg switch
                        {
                            "left" => (-1, 0),
                            "right" => (1, 0),
                            "up" => (0, -1),
                            "down" => (0, 1),
                            _ => (0, 0),
                        };

                        _ = command.Name == "float-move"
                            ? _model.NudgeFloat(session.Client, dx, dy, 0, 0)
                            : _model.NudgeFloat(session.Client, 0, 0, dx, dy);
                        break;
                    }

                case "float-embed":
                    if (_model.View(session.Client)?.Focused is { } focused)
                    {
                        _ = _model.ToTile(focused) || _model.ToFloat(focused);
                    }

                    break;
                case "focus-in" or "focus-out":
                    {
                        if (FocusedRuntime(session.Client) is { Modes.FocusEvents: true } pane)
                        {
                            pane.Send(command.Name == "focus-in" ? "\e[I"u8.ToArray() : "\e[O"u8.ToArray());
                        }

                        break;
                    }
            }

            ApplyResizes();
        }

        _wake.Release();
    }

    private void CopyStep(AttachSession session, string step)
    {
        if (session.Copy is not { } copy || !_runtimes.TryGetValue(copy.Pane, out var pane))
        {
            session.Copy = null;
            return;
        }

        string? text;
        lock (pane.Gate)
        {
            text = copy.Apply(step, pane.Terminal, pane.Screen.Cols);
            pane.Dirty = true;
        }

        if (text is { Length: > 0 })
        {
            _copies.Enqueue((pane.Id, text));
        }

        if (copy.Done)
        {
            session.Copy = null;
        }
    }

    public const int ResizeCells = 5;

    private void SplitFocused(string client, bool sideBySide)
    {
        if (_model.View(client) is not { Focused: { } focused } view || view.Panes.All(p => p.Pane != focused))
        {
            return;
        }

        var cwd = _model.Pane(focused)?.Cwd ?? Environment.CurrentDirectory;
        if (_model.Split(focused, sideBySide, newFirst: false, 50, cwd, []) is { } pane)
        {
            ApplyResizes();
            StartQuietly(pane, client);
        }
    }

    private void NewTab(string client)
    {
        if (_model.View(client) is not { Workspace: { } workspace } view)
        {
            return;
        }

        var cwd = view.Focused is { } focused ? _model.Pane(focused)?.Cwd : null;
        var pane = _model.Spawn(workspace.Name, cwd ?? Environment.CurrentDirectory, []);
        ApplyResizes();
        StartQuietly(pane, client);
    }

    private void StartQuietly(PaneState pane, string client)
    {
        try
        {
            Start(pane, new Dictionary<string, string> { [ClientVariable] = client });
        }
        catch (InvalidOperationException e)
        {
            options.Log(e.Message);
        }
    }

    private void SmartFocus(AttachSession session, CommandMessage command)
    {
        var (dx, dy) = Direction(command.Arg);
        bool nvim;

        lock (_gate)
        {
            _model.Touch(session.Client);
            nvim = _model.View(session.Client)?.Focused is { } focused && _model.IsNvim(focused);

            if (!nvim)
            {
                _model.FocusDirection(session.Client, dx, dy);
                ApplyResizes();
            }
        }

        if (nvim)
        {
            Forward(session, command);
        }

        _wake.Release();
    }

    private void Newline(AttachSession session, CommandMessage command)
    {
        PaneRuntime? target;
        bool nvim;

        lock (_gate)
        {
            _model.Touch(session.Client);
            target = FocusedRuntime(session.Client);
            nvim = target is not null && _model.IsNvim(target.Id);
        }

        if (target is null)
        {
            return;
        }

        if (target.Modes.Win32Input)
        {
            target.Send(nvim
                ? [.. Input.ConPtyModes.Encode(0x0D, 0x1C, 0x0D, true, 0x02, 1),
                    .. Input.ConPtyModes.Encode(0x0D, 0x1C, 0x0D, false, 0x02, 1)]
                : [.. Input.ConPtyModes.Encode(0x4A, 0x24, 0x0A, true, 0x08, 1),
                    .. Input.ConPtyModes.Encode(0x4A, 0x24, 0x0A, false, 0x08, 1)]);
            return;
        }

        target.Send(nvim ? "\e\r"u8.ToArray() : "\n"u8.ToArray());
    }

    private void Forward(AttachSession session, CommandMessage command)
    {
        if (command.Key is { } key)
        {
            Route(session, key);
        }
        else if (command.Bytes is { } bytes)
        {
            Route(session, new TextMessage { Bytes = bytes });
        }
    }

    public static (int Dx, int Dy) Direction(string? name) => name?.ToLowerInvariant() switch
    {
        "left" => (-1, 0),
        "right" => (1, 0),
        "up" => (0, -1),
        "down" => (0, 1),
        _ => (0, 0),
    };

    private void NextWorkspace(string client)
    {
        if (_model.NextProject(client) is { } next)
        {
            Show(client, next, take: false);
        }
    }

    private void OpenWindow(string? client, string? workspace)
    {
        if (client is null || workspace is null || _model.Workspace(workspace) is null)
        {
            throw new InvalidOperationException("open-window needs a client and an open workspace");
        }

        foreach (var other in _model.Clients.ToList())
        {
            _model.Release(other.Id, workspace);
        }

        if (_sessions.TryGetValue(client, out var session))
        {
            session.Pending.Enqueue(new HostEffect { Kind = HostEffects.OpenWindow, Value = workspace });
        }

        ApplyResizes();
        options.Log($"{client} opens {workspace} in a new window");
    }

    public const string MenuTitle = "fleet menu";

    public const string NothingLeft = "no project left in this window";

    public static readonly TimeSpan RevealAnyway = TimeSpan.FromMilliseconds(2500);

    public static readonly TimeSpan RevealAfterFit = TimeSpan.FromMilliseconds(1200);

    private volatile bool _revealing;

    private (int Cols, int Rows)? _menuFit;

    private void Fit(string pane, int cols, int rows, ControlRequest request)
    {
        var before = _model.FloatBounds(pane);
        Require(_model.FitFloat(pane, cols, rows), request);
        options.Log($"{pane} fit {cols}x{rows}: {before?.Width}x{before?.Height} -> {_model.FloatBounds(pane)?.Width}x{_model.FloatBounds(pane)?.Height}");

        if (_model.Pane(pane)?.Args is [_, "menu", ..])
        {
            _menuFit = (cols, rows);
        }

        if (_model.Float(pane) is not { } box || !_runtimes.TryGetValue(pane, out var runtime))
        {
            return;
        }

        if (box.Hidden)
        {
            box.RevealAfterOutput = runtime.Outputs;
            box.HiddenSince = DateTime.UtcNow - RevealAnyway + RevealAfterFit;
            box.NeedsBaseline = before != _model.FloatBounds(pane);
            box.Baseline = null;
            return;
        }

        if (box.Held is null && before is { } shown && shown != box.Bounds)
        {
            box.Held = shown;
            box.HeldLabel = _model.FloatLabel(box);
        }

        if (box.Held is not null)
        {
            box.ReleaseAfterOutput = runtime.Outputs;
            box.HeldSince = DateTime.UtcNow - RevealAnyway + RevealAfterFit;
        }
    }

    private void Hold(string pane)
    {
        if (_model.Float(pane) is { Hidden: false } box)
        {
            options.Log($"{pane} held");
            box.HeldLabel ??= _model.FloatLabel(box);
            box.Held ??= box.Bounds;
            box.HeldSince = DateTime.UtcNow;
            box.ReleaseAfterOutput = -1;
            _revealing = true;
        }
    }

    private void ReleaseHeldFloats()
    {
        foreach (var box in _model.HeldFloats().ToList())
        {
            var redrawn = box.ReleaseAfterOutput >= 0
                          && _runtimes.TryGetValue(box.Pane, out var runtime)
                          && runtime.Outputs > box.ReleaseAfterOutput
                          && Environment.TickCount64 - runtime.LastOutputAt >= options.RevealWhenQuiet.TotalMilliseconds;

            if (redrawn || DateTime.UtcNow - box.HeldSince > RevealAnyway)
            {
                options.Log($"{box.Pane} released {(redrawn ? "after its redraw" : "after waiting")} at {box.Bounds.Width}x{box.Bounds.Height}");
                box.Held = null;
                box.HeldLabel = null;
                box.ReleaseAfterOutput = -1;
            }
            else
            {
                _revealing = true;
            }
        }
    }

    private static string Signature(ScreenBuffer screen)
    {
        var text = new StringBuilder();
        for (var y = 0; y < screen.Rows; y++)
        {
            foreach (var cell in screen.Row(y))
            {
                text.Append(cell.Text);
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static bool HasContent(ScreenBuffer screen)
    {
        var rows = 0;
        for (var y = 0; y < screen.Rows && rows < 2; y++)
        {
            foreach (var cell in screen.Row(y))
            {
                if (cell.Text.Length > 0 && !string.IsNullOrWhiteSpace(cell.Text) && cell.Text != "\0")
                {
                    rows++;
                    break;
                }
            }
        }

        return rows >= 2;
    }

    private void RevealReadyFloats()
    {
        foreach (var box in _model.HiddenFloats().ToList())
        {
            _runtimes.TryGetValue(box.Pane, out var runtime);

            if (box.NeedsBaseline && runtime is not null)
            {
                box.Baseline = Signature(runtime.Screen);
                box.NeedsBaseline = false;
            }

            var drawn = box.RevealAfterOutput >= 0
                        && runtime is not null
                        && runtime.Outputs > box.RevealAfterOutput
                        && HasContent(runtime.Screen)
                        && (box.Baseline is null || Signature(runtime.Screen) != box.Baseline)
                        && Environment.TickCount64 - runtime.LastOutputAt >= options.RevealWhenQuiet.TotalMilliseconds;

            if (drawn || DateTime.UtcNow - box.HiddenSince > RevealAnyway)
            {
                box.Hidden = false;
            }
            else
            {
                _revealing = true;
            }
        }
    }


    private readonly Dictionary<string, DateTime> _warmedAt = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan WarmAgainAfter = TimeSpan.FromSeconds(2);

    private List<string> MenuArgs(string? project) =>
        project is null ? [options.FleetExecutable, "menu"] : [options.FleetExecutable, "menu", "--project", project];

    private bool HasMenu(string workspace) =>
        _model.PanesIn(workspace).Any(id => _model.Pane(id)?.Args is [_, "menu", ..]);

    private void WarmMenus()
    {
        foreach (var stranded in _model.StrandedParked())
        {
            Kill(stranded);
        }

        if (!options.WarmMenus)
        {
            return;
        }

        var shown = _model.Clients
            .Select(c => c.Showing)
            .OfType<string>()
            .Where(w => !Fleet.Shared.Constants.FleetWorkspaces.IsHidden(w) && _model.Workspace(w) is { Tabs.Count: > 0 })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var workspace in shown)
        {
            if (HasMenu(workspace)
                || (_warmedAt.TryGetValue(workspace, out var at) && DateTime.UtcNow - at < WarmAgainAfter))
            {
                continue;
            }

            _warmedAt[workspace] = DateTime.UtcNow;
            var (cols, rows) = _model.Clients
                .Where(c => string.Equals(c.Showing, workspace, StringComparison.OrdinalIgnoreCase))
                .Select(c => (c.Cols, c.Rows))
                .First();

            var menu = _model.SpawnParked(
                workspace, Environment.CurrentDirectory, MenuArgs(workspace), MuxModel.OverlayArea(cols, rows));
            _model.SetTitle(menu.Id, MenuTitle);
            if (_menuFit is var (fitCols, fitRows))
            {
                _model.FitFloat(menu.Id, fitCols, fitRows);
            }

            ApplyResizes();

            try
            {
                Start(menu, null);
                options.Log($"warm menu {menu.Id} for {workspace}");
            }
            catch (InvalidOperationException e)
            {
                options.Log($"warm menu for {workspace}: {e.Message}");
            }
        }
    }

    private void OpenMenu(string client, string? action)
    {
        if (_model.Client(client) is not { Overlay: null } state)
        {
            return;
        }

        var project = state.Showing is { } s && !Fleet.Shared.Constants.FleetWorkspaces.IsHidden(s) ? s : null;
        var args = MenuArgs(project);

        if (action is not null)
        {
            args.AddRange(["--action", action]);
        }

        var env = new Dictionary<string, string> { [ClientVariable] = client };

        if (state.Showing is { } shown && _model.Workspace(shown) is not null)
        {
            if (state.Menu is { } open && _model.FloatBounds(open) is not null)
            {
                _model.Focus(open);
                return;
            }

            if (action is null && _model.Parked(shown) is { } warm && _model.Unpark(warm.Pane))
            {
                state.Menu = warm.Pane;
                options.Log($"{client}: warm menu {warm.Pane}{(warm.Hidden ? " (still drawing)" : string.Empty)}");
                return;
            }

            var menu = _model.SpawnFloat(
                shown, Environment.CurrentDirectory, args, MuxModel.OverlayArea(state.Cols, state.Rows), modal: true);
            _model.SetTitle(menu.Id, MenuTitle);

            if (_menuFit is var (fitCols, fitRows))
            {
                _model.FitFloat(menu.Id, fitCols, fitRows);
            }

            if (_model.Float(menu.Id) is { } hidden)
            {
                hidden.Hidden = true;
                hidden.HiddenSince = DateTime.UtcNow;
            }
            state.Menu = menu.Id;
            ApplyResizes();
            Start(menu, env);
            return;
        }

        var pane = _model.Spawn(MuxModel.OverlayWorkspace, Environment.CurrentDirectory, args);
        _model.SetOverlay(client, pane.Id);
        ApplyResizes();
        Start(pane, env);
    }

    private double Show(string? client, string? workspace, bool take = true)
    {
        var clock = Stopwatch.StartNew();

        if (client is null || workspace is null)
        {
            throw new InvalidOperationException("show needs a client and a workspace");
        }

        var shown = take && MuxModel.IsProject(workspace)
            ? _model.Take(client, workspace)
            : _model.Show(client, workspace);

        if (!shown)
        {
            throw new InvalidOperationException($"no client {client} or no workspace {workspace}");
        }

        ApplyResizes();

        if (_sessions.TryGetValue(client, out var session))
        {
            session.Switching = clock;
        }

        options.Log($"show {workspace} for {client}");
        return clock.Elapsed.TotalMilliseconds;
    }

    private string Spawn(ControlRequest request)
    {
        var workspace = request.Workspace
            ?? (request.NewWindow ? request.Session : request.Window ?? request.Session)
            ?? CallerWorkspace(request.Caller)
            ?? Fleet.Shared.Constants.FleetWorkspaces.Default;

        var pane = _model.Spawn(workspace, request.Cwd ?? Environment.CurrentDirectory, request.Args ?? []);
        ApplyResizes();
        Start(pane, request.Env);
        return pane.Id;
    }

    private string SpawnFloat(ControlRequest request)
    {
        if (request.Pane is { Length: > 0 } over)
        {
            var home = CallerWorkspace(over) ?? throw new InvalidOperationException($"no pane {over}");
            var below = _model.PaneArea(over) ?? _model.FloatBounds(over);
            var box = _model.SpawnFloat(
                home,
                request.Cwd ?? _model.Pane(over)?.Cwd ?? Environment.CurrentDirectory,
                request.Args ?? [],
                below is { } area ? MuxModel.Over(area) : null,
                modal: true);
            ApplyResizes();
            Start(box, request.Env);
            return box.Id;
        }

        var workspace = request.Workspace
            ?? request.Session
            ?? CallerWorkspace(request.Caller)
            ?? Fleet.Shared.Constants.FleetWorkspaces.Default;

        var pane = _model.SpawnFloat(workspace, request.Cwd ?? Environment.CurrentDirectory, request.Args ?? []);
        ApplyResizes();
        Start(pane, request.Env);
        return pane.Id;
    }

    private void NewFloat(string client)
    {
        if (_model.View(client) is not { Workspace: { } workspace } view)
        {
            return;
        }

        var cwd = view.Focused is { } focused && _model.Pane(focused) is { } pane
            ? pane.Cwd
            : Environment.CurrentDirectory;

        var created = _model.SpawnFloat(workspace.Name, cwd, []);
        ApplyResizes();

        try
        {
            Start(created, new Dictionary<string, string> { [ClientVariable] = client });
        }
        catch (InvalidOperationException e)
        {
            options.Log(e.Message);
        }
    }

    private bool DragFloat(MouseCapture capture, int x, int y)
    {
        var start = capture.Start;
        var dx = x - capture.FromX;
        var dy = y - capture.FromY;

        return capture.Kind == MouseHitKind.FloatMove
            ? _model.MoveFloat(capture.Pane!, start.X + dx, start.Y + dy)
            : _model.ResizeFloat(capture.Pane!, start.Width + dx, start.Height + dy)
              && _model.MoveFloat(capture.Pane!, start.X, start.Y);
    }

    private string Split(ControlRequest request)
    {
        var source = Pane(request);
        var (sideBySide, newFirst) = request.Direction switch
        {
            "left" => (true, true),
            "top" => (false, true),
            "bottom" => (false, false),
            _ => (true, false),
        };

        if (request.MovePane is { Length: > 0 } moving)
        {
            Require(_model.JoinBeside(source, moving, sideBySide, newFirst, request.Percent), request);
            return moving;
        }

        var cwd = request.Cwd ?? _model.Pane(source)?.Cwd ?? Environment.CurrentDirectory;
        var pane = _model.Split(source, sideBySide, newFirst, request.Percent, cwd, request.Args ?? [])
            ?? throw new InvalidOperationException($"no pane {source}");
        ApplyResizes();
        Start(pane, request.Env);
        return pane.Id;
    }

    private string? ClientFor(ControlRequest request, string? attachedClient) =>
        request.Client ?? attachedClient ?? _model.Clients.FirstOrDefault(c => c.Menu is { } menu && menu == request.Caller)?.Id;

    private string MoveTarget(ControlRequest request) =>
        request.Workspace ?? request.Window ?? request.Session
        ?? throw new InvalidOperationException("move needs a workspace or a window");

    private string? CallerWorkspace(string? caller) =>
        caller is null ? null : _model.ListPanes().FirstOrDefault(p => p.Id.Value == caller)?.SessionName;

    private void Start(PaneState pane, IReadOnlyDictionary<string, string>? requested)
    {
        pane.Env = (requested ?? new Dictionary<string, string>())
            .Where(e => e.Key != ClientVariable)
            .ToDictionary(e => e.Key, e => e.Value);

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ProfileEnv(pane.Cwd))
        {
            env[key] = value;
        }

        foreach (var (key, value) in requested ?? new Dictionary<string, string>())
        {
            env[key] = value;
        }

        env = new Dictionary<string, string>(env, StringComparer.OrdinalIgnoreCase)
        {
            ["FLEET_MUX"] = "embedded",
            [PaneVariable] = pane.Id,
            [Endpoint.Variable] = options.Endpoint.Address,
            ["WEZTERM_PANE"] = string.Empty,
            ["WEZTERM_EXECUTABLE"] = options.FleetExecutable,
            [FloatPane.Variable] = _model.FloatBounds(pane.Id) is not null || _model.IsOverlay(pane.Id) ? "1" : string.Empty,
            [FramedPane.Variable] = MuxModel.IsDashboard(pane) ? "1" : string.Empty,
            ["WEZTERM_UNIX_SOCKET"] = string.Empty,
            ["TMUX"] = string.Empty,
        };

        var cols = Math.Max(pane.Cols, 2);
        var rows = Math.Max(pane.Rows, 2);
        var pty = options.Pty();
        var runtime = new PaneRuntime(pane.Id, pty, options.Terminal, cols, rows);
        _runtimes[pane.Id] = runtime;

        runtime.Terminal.TitleChanged += () =>
        {
            runtime.TitleDirty = true;
            _wake.Release();
        };

        runtime.Terminal.Copied += text =>
        {
            _copies.Enqueue((pane.Id, text));
            _wake.Release();
        };

        pty.Output += (buffer, count) =>
        {
            if (runtime.Feed(buffer, count))
            {
                options.Log($"{pane.Id} modes: {runtime.ModeSummary}");
            }

            _wake.Release();
        };

        pty.Exited += code =>
        {
            runtime.Exited = true;
            options.Log($"{pane.Id} exited with {code}");
            _ = Task.Run(() =>
            {
                lock (_gate)
                {
                    if (_runtimes.TryGetValue(pane.Id, out var current) && current == runtime)
                    {
                        Kill(pane.Id);
                        ApplyResizes();
                    }
                }

                _wake.Release();
            });
        };

        var command = pane.Args.Count > 0 ? pane.Args : options.Shell();
        var (program, args) = command.Count > 0
            ? (command[0], command.Skip(1).ToList())
            : (DefaultShell(), new List<string>());

        try
        {
            pty.Start(program, args, cols, rows, pane.Cwd, env);
            options.Log($"started {pane.Id}: {program} in {pane.Cwd} at {cols}x{rows}");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            _runtimes.Remove(pane.Id);
            _model.Kill(pane.Id);
            runtime.Dispose();
            throw new InvalidOperationException($"could not start {program}: {e.Message}");
        }
    }

    private IReadOnlyDictionary<string, string> ProfileEnv(string cwd)
    {
        try
        {
            return options.PaneEnv(cwd) ?? new Dictionary<string, string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            options.Log($"pane env for {cwd}: {e.Message}");
            return new Dictionary<string, string>();
        }
    }

    private bool Kill(string? id)
    {
        if (id is null || !_model.Kill(id))
        {
            return false;
        }

        if (_runtimes.Remove(id, out var runtime))
        {
            runtime.Dispose();
        }

        return true;
    }

    private void ApplyResizes()
    {
        foreach (var (pane, cols, rows) in _model.Resizes())
        {
            if (_runtimes.TryGetValue(pane.Id, out var runtime))
            {
                runtime.Resize(cols, rows);
            }
        }
    }

    private PaneRuntime? FocusedRuntime(string client) =>
        _model.View(client)?.Focused is { } focused ? _runtimes.GetValueOrDefault(focused) : null;

    private PaneRuntime Runtime(ControlRequest request) =>
        _runtimes.GetValueOrDefault(Pane(request))
        ?? throw new InvalidOperationException($"no pane {request.Pane}");

    private static string Pane(ControlRequest request) =>
        request.Pane is { Length: > 0 } pane ? pane : throw new InvalidOperationException($"{request.Op} needs a pane");

    private static void Require(bool done, ControlRequest request)
    {
        if (!done)
        {
            throw new InvalidOperationException($"{request.Op} failed for pane {request.Pane}");
        }
    }

    private void Detach(AttachSession session)
    {
        lock (_gate)
        {
            if (_model.Client(session.Client)?.Overlay is { } overlay)
            {
                Kill(overlay);
            }

            _model.Disconnect(session.Client);
            _sessions.Remove(session.Client);
            ApplyResizes();
        }

        options.Log($"client {session.Client} detached");
        _wake.Release();
    }

    private async Task RenderLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _wake.WaitAsync(TimeSpan.FromMilliseconds(_revealing ? 15 : 250), ct).ConfigureAwait(false);
                await Task.Delay(options.FrameDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (_wake.CurrentCount > 0)
            {
                _wake.Wait(0, CancellationToken.None);
            }

            List<(AttachSession Session, long Seq, bool Full, string Bytes, Stopwatch? Switching)> sends = [];
            List<(AttachSession Session, HostEffect Effect)> effects = [];
            List<AttachSession> farewells = [];
            string? persisted;

            lock (_gate)
            {
                _revealing = false;
                ReleaseHeldFloats();

                foreach (var runtime in _runtimes.Values)
                {
                    if (_model.Float(runtime.Id) is not { Held: not null })
                    {
                        runtime.Snapshot();
                    }

                    if (runtime.TitleDirty && _model.Pane(runtime.Id) is { } state)
                    {
                        runtime.TitleDirty = false;
                        lock (runtime.Gate)
                        {
                            state.Title = runtime.Terminal.Title;
                        }
                    }
                }

                RevealReadyFloats();

                while (_copies.TryDequeue(out var copy))
                {
                    if (CopyTarget(copy.Pane) is { } target)
                    {
                        effects.Add((target, new HostEffect { Kind = HostEffects.Clipboard, Value = copy.Text }));
                        options.Log($"{copy.Pane} copied {copy.Text.Length} chars to {target.Client}'s clipboard");
                    }
                }

                foreach (var session in _sessions.Values)
                {
                    while (session.Pending.TryDequeue(out var pending))
                    {
                        effects.Add((session, pending));
                    }

                    if (_model.WindowTitle(session.Client) is { } title && title != session.Title)
                    {
                        session.Title = title;
                        effects.Add((session, new HostEffect { Kind = HostEffects.Title, Value = title }));
                    }
                }

                foreach (var session in _sessions.Values)
                {
                    if (_model.Client(session.Client) is { Leaving: true } && !session.SaidBye)
                    {
                        session.SaidBye = true;
                        farewells.Add(session);
                        options.Log($"{session.Client} has no project left; sending it home");
                        continue;
                    }

                    if (_model.View(session.Client) is not { } view)
                    {
                        continue;
                    }

                    var copying = session.Copy is { } copy && _runtimes.TryGetValue(copy.Pane, out var copied)
                        ? copy.Overlay(copied.Screen.Viewport)
                        : null;
                    var frame = Composer.Compose(
                        view, id => _runtimes.GetValueOrDefault(id)?.Screen, session.Badge, copying, session.WhichKey);
                    if (session.Shown is not null && Same(session.Shown, frame))
                    {
                        continue;
                    }

                    var full = session.Shown is null
                        || session.Shown.Cols != frame.Cols
                        || session.Shown.Rows != frame.Rows;
                    sends.Add((session, ++session.Seq, full, FrameEncoder.Encode(session.Shown, frame), session.Switching));
                    session.Shown = frame;
                    session.Switching = null;
                }

                WarmMenus();

                if (_runtimes.Keys.Any(id => _model.Float(id) is not { Parked: true }) || _sessions.Count > 0)
                {
                    _lastBusy = DateTime.UtcNow;
                }
                else if (options.ExitWhenEmptyAfter is { } grace && DateTime.UtcNow - _lastBusy > grace)
                {
                    options.Log("nothing left to run; fleetd exits");
                    _stop.Cancel();
                }

                persisted = SessionDue() ? SessionJson() : null;
            }

            if (persisted is not null)
            {
                SaveSession(persisted);
            }

            foreach (var session in farewells)
            {
                try
                {
                    await session.Wire.SendAsync(MessageType.Bye, Encoding.UTF8.GetBytes(NothingLeft), ct).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
                {
                }
            }

            foreach (var (session, effect) in effects)
            {
                try
                {
                    await session.Wire.SendAsync(MessageType.HostEffect, effect, WireJsonContext.Default.HostEffect, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
                {
                }
            }

            foreach (var (session, seq, full, bytes, switching) in sends)
            {
                try
                {
                    await session.Wire.SendFrameAsync(seq, full, bytes, ct).ConfigureAwait(false);
                    if (switching is not null)
                    {
                        options.Log($"switch frame for {session.Client}: {switching.ElapsedMilliseconds} ms, {bytes.Length} bytes");
                    }
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
                {
                }
            }
        }
    }

    private void RestoreSession()
    {
        if (options.SessionFile is not { } file || !File.Exists(file))
        {
            return;
        }

        SessionSnapshot? snapshot;
        try
        {
            var json = File.ReadAllText(file);
            snapshot = JsonSerializer.Deserialize(json, SessionJsonContext.Default.SessionSnapshot);
            File.Copy(file, Path.ChangeExtension(file, ".previous.json"), overwrite: true);
            _savedSession = json;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            options.Log($"could not read the saved session {file}: {e.Message}");
            return;
        }

        if (snapshot is null)
        {
            return;
        }

        lock (_gate)
        {
            var panes = _model.Restore(snapshot, Relaunch);
            _model.Resizes();

            var started = 0;
            foreach (var pane in panes)
            {
                try
                {
                    Start(pane, pane.Env);
                    started++;
                }
                catch (InvalidOperationException e)
                {
                    options.Log($"restore: {e.Message}");
                }
            }

            options.Log($"restored {started} of {panes.Count} panes from {file}");
        }
    }

    private IReadOnlyList<string> Relaunch(IReadOnlyList<string> args) =>
        AgentHarness.Resumed(args.Count > 0 && Path.GetFileNameWithoutExtension(args[0]) == "fleet"
            ? [options.FleetExecutable, .. args.Skip(1)]
            : args);

    private bool SessionDue() =>
        options.SessionFile is not null && DateTime.UtcNow - _lastSave >= options.SaveEvery;

    private string SessionJson()
    {
        _lastSave = DateTime.UtcNow;
        var snapshot = _model.Snapshot();
        return snapshot.Workspaces.Count == 0
            ? string.Empty
            : JsonSerializer.Serialize(snapshot, SessionJsonContext.Default.SessionSnapshot);
    }

    private void SaveSession(string json)
    {
        if (json == _savedSession || options.SessionFile is not { } file)
        {
            return;
        }

        try
        {
            if (json.Length == 0)
            {
                File.Delete(file);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
                var temporary = file + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, file, overwrite: true);
            }

            _savedSession = json;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            options.Log($"could not save the session to {file}: {e.Message}");
        }
    }

    private AttachSession? CopyTarget(string pane)
    {
        var showing = _sessions.Values
            .Select(s => (Session: s, View: _model.View(s.Client)))
            .Where(x => x.View is not null
                        && (x.View.Panes.Any(p => p.Pane == pane)
                            || x.View.FloatingPanes.Any(p => p.Pane == pane)
                            || x.View.Overlay?.Pane == pane))
            .OrderByDescending(x => x.View!.Client.LastActive)
            .Select(x => x.Session)
            .FirstOrDefault();

        return showing ?? _sessions.Values
            .OrderByDescending(s => _model.Client(s.Client)?.LastActive ?? 0)
            .FirstOrDefault();
    }

    private static bool Same(ClientFrame a, ClientFrame b) =>
        a.Cols == b.Cols && a.Rows == b.Rows
        && a.CursorX == b.CursorX && a.CursorY == b.CursorY
        && a.CursorVisible == b.CursorVisible && a.CursorShape == b.CursorShape
        && a.Cells.AsSpan().SequenceEqual(b.Cells);

    private void Shutdown()
    {
        lock (_gate)
        {
            foreach (var runtime in _runtimes.Values)
            {
                runtime.Dispose();
            }

            _runtimes.Clear();
        }
    }

    public static string DefaultShell()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        }

        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
    }

    private sealed class AttachSession(string client, Wire wire)
    {
        public string Client { get; } = client;

        public Wire Wire { get; } = wire;

        public ClientFrame? Shown { get; set; }

        public string? Badge { get; set; }

        public long Seq { get; set; }

        public Stopwatch? Switching { get; set; }

        public MouseCapture? Capture { get; set; }

        public string? Title { get; set; }

        public CopySession? Copy { get; set; }

        public List<WhichKeyEntry>? WhichKey { get; set; }

        public bool SaidBye { get; set; }

        public System.Collections.Concurrent.ConcurrentQueue<HostEffect> Pending { get; } = new();
    }

    private sealed record MouseCapture(
        string? Pane,
        int Divider,
        MouseHitKind Kind = MouseHitKind.Pane,
        Rect Start = default,
        int FromX = 0,
        int FromY = 0);
}
