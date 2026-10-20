using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Fleet.Platform.Clipboard;
using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Platform.Mux.Embedded.Pty;
using Fleet.Platform.Mux.Embedded.Render;
using Fleet.Ports.Remotes.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Iso;
using Fleet.Shared.Iso.Models;

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

    public TimeSpan RevealWhenQuietRemote { get; init; } = TimeSpan.FromMilliseconds(15);

    public bool WarmMenus { get; init; }

    public TimeProvider Clock { get; init; } = TimeProvider.System;

    public bool TimeMenus { get; init; } = MenuTiming.On(Environment.GetEnvironmentVariable(MenuTiming.Variable));

    public bool TimeKeys { get; init; } = KeyLatency.On(Environment.GetEnvironmentVariable(KeyLatency.Variable));

    public Func<string, IReadOnlyDictionary<string, string>?> PaneEnv { get; init; } = _ => null;

    public Action<IDictionary<string, string>>? RemotePaneEnv { get; init; }

    public bool ServesBridge { get; init; }

    public string? BridgedFile { get; init; }

    public ClipboardTrace ClipboardTrace { get; init; } = ClipboardTrace.FromEnvironment();

    public Func<byte[], string?>? DropClipboardImage { get; init; }

    public ClipboardImageLimit ClipboardImageLimit { get; init; } = new(Reads: 20, Per: TimeSpan.FromMinutes(1));

    public ClipboardImageLimit ClipboardCopyLimit { get; init; } = new(Reads: 120, Per: TimeSpan.FromMinutes(1));

    public Func<IReadOnlyList<string>> Shell { get; init; } = () => [FleetDaemon.DefaultShell()];

    public Func<string, string, RemoteChannel>? RemoteOpen { get; init; }

    public Func<IReadOnlyList<NoticeDto>> Notices { get; init; } = () => [];

    public Func<IReadOnlyList<string>> SavedProjects { get; init; } = () => [];

    public Func<string, Task<string?>> OpenProject { get; init; } = name => Task.FromResult<string?>($"{name} cannot be opened here");

    public Action<string, IReadOnlyList<string>> DismissNotices { get; init; } = (_, _) => { };

    public Func<(bool Bell, bool Toast)> AlertSettings { get; init; } = () => (false, false);

    public Action<string, string> Toast { get; init; } = (_, _) => { };

    public Func<string, IReadOnlyDictionary<string, string>, CancellationToken, Task<(string Text, bool Failed)>>? Head { get; init; }

    public Func<RemoteProjectRequest, CancellationToken, Task<(string Text, bool Failed)>>? CreateProject { get; init; }

    public Func<IsoConfig> Iso { get; init; } = () => IsoConfig.Off;

    public Func<string, IEnumerable<string>> Worktrees { get; init; } = _ => [];

    public TimeSpan IsoSweepEvery { get; init; } = TimeSpan.FromSeconds(1);

    public ForwardOptions Forwards { get; init; } = new();

    public Func<IReadOnlyList<ProjectConfigDto>> ProjectConfigs { get; init; } = () => [];
}

public sealed record ClipboardImageLimit(int Reads, TimeSpan Per);

public sealed class FleetDaemon(DaemonOptions options)
{
    public const string ClientVariable = "FLEET_CLIENT";
    public const string PaneVariable = "FLEET_PANE";
    public const string ExecutableVariable = "FLEET_EXECUTABLE";
    public const string PaneTokenVariable = "FLEET_PANE_TOKEN";

    private readonly Lock _gate = new();
    private readonly MuxModel _model = new();
    private readonly Dictionary<string, PaneRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AttachSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Pane, string Text)> _copies = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, RemoteLink> _remotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Hello> _bridged = new(StringComparer.Ordinal);
    private readonly ForwardHub _hub = new(options.Forwards, options.Log);
    private readonly Dictionary<string, List<ForwardDto>> _viewerForwards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sshOf = new(StringComparer.Ordinal);
    private readonly HashSet<string> _cannotForward = new(StringComparer.Ordinal);
    private readonly HashSet<string> _tunnelsItself = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ViewerAnswer> _viewerAnswers = new(StringComparer.Ordinal);
    private readonly MenuTiming? _timing = options.TimeMenus ? new MenuTiming(options.Clock) : null;

    private readonly KeyLatency? _keyTiming = options.TimeKeys ? new KeyLatency(options.Clock) : null;
    private DateTime _lastBusy = DateTime.UtcNow;
    private DateTime _lastSave = DateTime.MinValue;
    private string? _savedSession;
    private volatile bool _forgotten;
    private readonly Lock _saveGate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (string Client, TaskCompletionSource<ClipboardImageResponse> Reply)> _clipboardAsks = new();
    private int _nextClipboardAsk;
    private volatile bool _servedBridge;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _paneTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _clipboardReads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _clipboardCopies = new(StringComparer.Ordinal);

    private bool ServesRemoteViewers => options.ServesBridge || _servedBridge;

    public MuxModel Model => _model;

    public void Stop() => _stop.Cancel();

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        var token = linked.Token;

        using var listener = options.Endpoint.Listen();
        options.Log($"fleetd listening on {options.Endpoint.Address}");

        MarkBridged(false);
        RestoreSession();

        var render = Task.Run(() => RenderLoopAsync(token), CancellationToken.None);
        var sweep = Task.Run(() => SweepViewsAsync(token), CancellationToken.None);

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
            await sweep.ConfigureAwait(false);
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
                            Host = Environment.MachineName,
                            Build = FleetVersion.Current,
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
                    case "float-buttons":
                        {
                            var pane = request.Pane ?? request.Caller ?? string.Empty;
                            Require(_model.SetFloatButtons(pane, [.. (request.Buttons ?? []).Select(BorderButtons.From)]), request);
                            Unhover(pane);
                            break;
                        }
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
                    case "notices":
                        {
                            var noticed = request.Workspace ?? string.Empty;
                            _model.SetNotices(noticed, request.Count);
                            response.Pending = _model.InAnyWindow(noticed);

                            if (request.Bell)
                            {
                                foreach (var session in _sessions.Values.Where(s => _model.Client(s.Client) is { } c && MuxModel.InWindow(c, noticed)))
                                {
                                    session.Pending.Enqueue(new HostEffect { Kind = HostEffects.Bell });
                                }
                            }

                            break;
                        }
                    case ForwardHub.ListOp:
                        response.Forwards = [.. _hub.List(), .. ViewerForwards()];
                        break;
                    case RemoteLink.ProjectConfigsOp:
                        response.ProjectConfigs = [.. options.ProjectConfigs()];
                        break;
                    case RemoteLink.ViewerForwardsOp:
                        if (ClientFor(request, attachedClient) is { } viewer)
                        {
                            _viewerForwards[viewer] = [.. (request.Forwards ?? []).Select(f => Viewed(f, request.Host ?? viewer))];
                        }

                        break;
                    case ForwardHub.ViewerOpenOp:
                        OpenOnViewer(request.Port);
                        break;
                    case ForwardHub.ViewerForwardOp:
                    case ForwardHub.ViewerUnforwardOp:
                        {
                            var client = ClientFor(request, attachedClient);
                            var shown = CallerWorkspace(request.Caller);
                            (response.Viewer, response.AwaitingReply) = ToViewer(
                                client,
                                shown,
                                request.Op == ForwardHub.ViewerForwardOp ? HostEffects.ForwardPort : HostEffects.UnforwardPort,
                                request.Port);
                            response.Ssh = response.Viewer is null ? SshOf(client, shown) : null;
                            if (response.AwaitingReply is { } awaiting)
                            {
                                _viewerAnswers[awaiting].Fallback = SshOf(client, shown);
                            }

                            break;
                        }
                    case ForwardHub.ViewerForwardedOp:
                        if (request.Reply is { } reply && _viewerAnswers.TryGetValue(reply, out var answered))
                        {
                            answered.Answer.TrySetResult(request.ForwardResult
                                ?? ForwardHub.ForwardResult(ForwardResults.Failed, string.Empty, "the viewer sent no result"));
                        }

                        break;
                    case "list-remotes":
                        response.Remotes = [.. _remotes.Values.Select(r => r.Snapshot())];
                        break;
                    case "remote-connect":
                        ConnectRemote(request.Host ?? throw new InvalidOperationException("remote-connect needs a host"));
                        break;
                    case "remote-answer":
                        RemoteFor(request.Host).Answer(request.Answer ?? string.Empty);
                        break;
                    case "remote-disconnect":
                        if (_remotes.Remove(request.Host ?? string.Empty, out var leaving))
                        {
                            leaving.Stop();
                            _hub.Dropped(leaving.Host);
                        }

                        break;
                    case "askpass":
                        {
                            var asking = _remotes.Values.FirstOrDefault(r => r.Token == request.Session)
                                ?? throw new InvalidOperationException("no remote is connecting with that token");
                            (response.Pending, response.Text) = asking.Ask(request.Text ?? string.Empty);
                            break;
                        }

                    case "show-remote":
                        response.Ms = ShowRemote(ClientFor(request, attachedClient), request.Host, request.Workspace);
                        break;
                    case "new-remote-project":
                        response.Ms = NewRemoteProject(ClientFor(request, attachedClient), request.Host);
                        break;
                    case "list-projects":
                        response.Projects = [.. options.SavedProjects()];
                        break;
                    case "open-project":
                        {
                            var opening = request.Workspace ?? throw new InvalidOperationException("open-project needs a project");
                            _ = Task.Run(async () =>
                            {
                                var failed = await options.OpenProject(opening).ConfigureAwait(false);
                                options.Log(failed is null ? $"opened {opening} for a remote viewer" : $"could not open {opening}: {failed}");
                            });
                            break;
                        }

                    case "list-notices":
                        response.Notices = [.. options.Notices()];
                        break;
                    case "dismiss-notices":
                        options.DismissNotices(request.Workspace ?? string.Empty, request.Args ?? []);
                        break;
                    case "remote-notices":
                        response.Notices = RemoteNotices(ClientFor(request, attachedClient));
                        break;
                    case "remote-dismiss":
                        RemoteFor(request.Host).Dismiss(request.Workspace ?? string.Empty, request.Args ?? []);
                        break;
                    case "window":
                        response.Window = Window(ClientFor(request, attachedClient));
                        break;
                    case "hand-back":
                        response.Pending = HandBack(ClientFor(request, attachedClient), request.Text);
                        break;
                    case "set-label":
                        if (_model.Client(ClientFor(request, attachedClient) ?? string.Empty) is { } labelled)
                        {
                            labelled.Label = request.Text;
                        }

                        break;
                    case "open-remote-window":
                        OpenRemoteWindow(ClientFor(request, attachedClient), request.Host, request.Workspace);
                        break;
                    case "shutdown":
                        if (options.SessionFile is not null)
                        {
                            lock (_saveGate)
                            {
                                _forgotten = true;
                                _savedSession = null;
                                SaveSessionLocked(string.Empty);
                            }
                        }

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
        using var gone = new CancellationTokenSource();
        AttachSession? session = null;

        try
        {
            if (await wire.ReceiveAsync(ct).ConfigureAwait(false) is not { Type: MessageType.Hello } first)
            {
                return;
            }

            var hello = Wire.Read(first.Payload, WireJsonContext.Default.Hello);
            if (Wire.Agree(hello.Version, hello.Highest) is not { } agreed)
            {
                await wire.SendAsync(
                    MessageType.Error,
                    new ErrorMessage { Message = Wire.Mismatch(hello.Version, hello.Highest, hello.Build) },
                    WireJsonContext.Default.ErrorMessage,
                    ct).ConfigureAwait(false);
                return;
            }

            var clientId = string.Empty;
            if (hello.Role == ClientRoles.Attach && !MayView(hello))
            {
                options.Log($"iso mode: {hello.Origin ?? "an unknown host"} connects without a view; attach is not allowed from there");
            }
            else if (hello.Role == ClientRoles.Attach)
            {
                lock (_gate)
                {
                    var client = _model.Connect(hello.Cols, hello.Rows, hello.Workspace);
                    client.Label = hello.Label;
                    client.Build = hello.Build;
                    clientId = client.Id;
                    session = new AttachSession(client.Id, wire);
                    _sessions[client.Id] = session;

                    if (hello.Bridged)
                    {
                        _servedBridge = true;
                        _bridged[client.Id] = hello;
                        MarkBridged(true);
                    }

                    if (hello.Ssh is { Length: > 0 } ssh)
                    {
                        _sshOf[client.Id] = ssh;
                    }

                    if (hello.Forwards is false)
                    {
                        _cannotForward.Add(client.Id);
                    }
                    else if (hello.Forwards is true && hello.Label is null)
                    {
                        _tunnelsItself.Add(client.Id);
                    }

                    Furnish(client.Id, hello);
                    ApplyResizes();
                }

                options.Log($"client {clientId} attached ({hello.Os}, {hello.Cols}x{hello.Rows})");
            }

            await wire.SendAsync(
                MessageType.Welcome,
                new Welcome { Version = agreed, Client = clientId, Build = FleetVersion.Current },
                WireJsonContext.Default.Welcome,
                ct).ConfigureAwait(false);

            if (session is not null)
            {
                session.Welcomed = true;
            }

            _wake.Release();

            while (!ct.IsCancellationRequested)
            {
                if (await wire.ReceiveAsync(ct).ConfigureAwait(false) is not { } message
                    || message.Type == MessageType.Bye)
                {
                    break;
                }

                if (session is not null && (!IsAttached(session) || !MayView(hello)))
                {
                    if (IsAttached(session))
                    {
                        LoseView(session, hello);
                    }

                    session = null;
                }

                await HandleAsync(wire, session, hello.Bridged, message.Type, message.Payload, gone.Token, ct).ConfigureAwait(false);
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
            await gone.CancelAsync().ConfigureAwait(false);

            if (session is not null)
            {
                Detach(session);
            }
        }
    }

    private bool IsAttached(AttachSession session)
    {
        lock (_gate)
        {
            return _sessions.ContainsKey(session.Client);
        }
    }

    private void LoseView(AttachSession session, Hello hello)
    {
        options.Log($"iso mode: {hello.Origin ?? "an unknown host"} lost its view; attach is not allowed from there");
        Detach(session);
    }

    private async Task SweepViewsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.IsoSweepEvery, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_bridged.IsEmpty)
            {
                continue;
            }

            foreach (var (client, hello) in _bridged)
            {
                AttachSession? viewing;
                lock (_gate)
                {
                    viewing = _sessions.GetValueOrDefault(client);
                }

                if (viewing is not null && !MayView(hello))
                {
                    LoseView(viewing, hello);
                }
            }
        }
    }

    private bool MayView(Hello hello) =>
        !hello.Bridged || options.Iso() is not { On: true } iso || iso.MayAttachFrom(hello.Origin);

    private async Task HandleAsync(
        Wire wire, AttachSession? session, bool bridged, MessageType type, byte[] payload, CancellationToken gone, CancellationToken ct)
    {
        switch (type)
        {
            case MessageType.Request:
                {
                    var request = Wire.Read(payload, WireJsonContext.Default.ControlRequest);
                    var iso = options.Iso();

                    if (request.Op == RemoteLink.HeadOp || (request.Op == RemoteHeadOp && !iso.On))
                    {
                        var redact = bridged && iso.On;
                        _ = Task.Run(() => AnswerHeadAsync(wire, request, redact, ct), CancellationToken.None);
                        break;
                    }

                    if ((request.Op == RemoteLink.CreateProjectOp && options.CreateProject is not null)
                        || (request.Op == RemoteCreateProjectOp && !iso.On))
                    {
                        var refuse = request.Op == RemoteLink.CreateProjectOp && bridged && iso.On;
                        _ = Task.Run(() => AnswerCreateProjectAsync(wire, request, refuse, ct), CancellationToken.None);
                        break;
                    }

                    if (request.Op == MenuWaitOp && !bridged)
                    {
                        _ = Task.Run(() => AnswerMenuWaitAsync(wire, request, gone, ct), CancellationToken.None);
                        break;
                    }

                    if (request.Op == ClipboardCopyOp)
                    {
                        var copied = bridged
                            ? new ControlResponse { Ok = false, Error = "clipboard copies are only taken from local panes" }
                            : CopyToViewer(request);
                        copied.Id = request.Id;
                        await wire.SendAsync(MessageType.Response, copied, WireJsonContext.Default.ControlResponse, ct)
                            .ConfigureAwait(false);
                        break;
                    }

                    if (request.Op is ClipboardImageOp or ClipboardTypesOp)
                    {
                        if (bridged)
                        {
                            await wire.SendAsync(
                                MessageType.Response,
                                new ControlResponse { Id = request.Id, Ok = false, Error = "clipboard images are only served to local panes" },
                                WireJsonContext.Default.ControlResponse,
                                ct).ConfigureAwait(false);
                            break;
                        }

                        _ = Task.Run(() => AnswerClipboardImageAsync(wire, request, ct), CancellationToken.None);
                        break;
                    }

                    if (RunsForward(request.Op, iso, bridged))
                    {
                        _ = Task.Run(() => AnswerForwardAsync(wire, request, ct), CancellationToken.None);
                        break;
                    }

                    var response = IsoRefusal(request.Op, iso) is { } refused
                        ? new ControlResponse { Id = request.Id, Ok = false, Error = refused }
                        : bridged && iso.On
                            ? ExecuteIso(request, session?.Client, iso)
                            : Execute(request, session?.Client);

                    if (response.AwaitingReply is { } awaiting)
                    {
                        _ = Task.Run(() => AnswerViewerForwardAsync(wire, request.Port, response, awaiting, ct), CancellationToken.None);
                        break;
                    }

                    await wire.SendAsync(MessageType.Response, response, WireJsonContext.Default.ControlResponse, ct)
                        .ConfigureAwait(false);
                    break;
                }

            case MessageType.Key or MessageType.Text or MessageType.Mouse or MessageType.Command
                when session is not null && RemoteTarget(session, type, payload) is { } remote:
                if (_timing is not null && type == MessageType.Command
                    && Wire.Read(payload, WireJsonContext.Default.CommandMessage).Name == "menu")
                {
                    _timing.Begin(session.Client, remote.Host);
                }

                await remote.ForwardAsync(type, payload).ConfigureAwait(false);
                break;

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

            case MessageType.ClipboardImageResponse when session is not null:
                {
                    var answer = Wire.Read(payload, WireJsonContext.Default.ClipboardImageResponse);
                    if (_clipboardAsks.TryGetValue(answer.Id, out var ask) && ask.Client == session.Client)
                    {
                        ask.Reply.TrySetResult(answer);
                    }

                    break;
                }
        }
    }

    public const string ClipboardImageOp = "clipboard-image";

    public const string ClipboardTypesOp = "clipboard-types";

    public const string ClipboardCopyOp = "clipboard-copy";

    public static readonly TimeSpan ClipboardImageWithin = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan RelayedClipboardImageWithin = TimeSpan.FromSeconds(15);

    private async Task AnswerClipboardImageAsync(Wire wire, ControlRequest request, CancellationToken ct)
    {
        var response = ClipboardCallerRefusal(request) is { } refused
            ? new ControlResponse { Ok = false, Error = refused }
            : await ClipboardImageForAsync(request.Pane, request.Op == ClipboardTypesOp, ct).ConfigureAwait(false);
        response.Id = request.Id;

        try
        {
            await wire.SendAsync(MessageType.Response, response, WireJsonContext.Default.ControlResponse, ct)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private async Task<ControlResponse> ClipboardImageForAsync(string? pane, bool probe, CancellationToken ct, TimeSpan? within = null, bool paste = false)
    {
        AttachSession? viewer;
        lock (_gate)
        {
            viewer = pane is { Length: > 0 } ? FocusingViewer(pane) : null;
        }

        if (viewer is null)
        {
            options.Log($"clipboard image for {pane ?? "an unnamed pane"} refused: not focused in any viewer");
            return new ControlResponse { Ok = false, Error = $"pane {pane} is not focused in any viewer" };
        }

        var answer = await AskClipboardImageAsync(viewer, pane!, probe, within ?? ClipboardImageWithin, ct, paste).ConfigureAwait(false);
        return answer switch
        {
            { Error: { } error } => new ControlResponse { Ok = false, Error = error },
            _ when probe => new ControlResponse { Ok = true, HasImage = answer.HasImage },
            { Data.Length: > ClipboardImageResponse.MaxBytes } => new ControlResponse { Ok = false, Error = ClipboardImageResponse.TooLarge(answer.Data.Length) },
            { Data: { Length: > 0 } png } => new ControlResponse { Ok = true, Mime = answer.Mime ?? ClipboardImageResponse.Png, Data = png, HasImage = true },
            _ => new ControlResponse { Ok = true },
        };
    }

    private async Task RelayClipboardImageAsync(RemoteLink link, ClipboardImageRequest asked)
    {
        string? shownHere;
        lock (_gate)
        {
            shownHere = _runtimes.Values.FirstOrDefault(r => r.Pty == link.Pty)?.Id;
        }

        var answer = await ClipboardImageForAsync(shownHere, asked.Probe, _stop.Token, RelayedClipboardImageWithin, asked.Paste).ConfigureAwait(false);
        await link.AnswerClipboardImageAsync(
            !answer.Ok ? new ClipboardImageResponse { Id = asked.Id, Error = answer.Error }
            : asked.Probe ? ClipboardImageResponse.Probed(asked.Id, answer.HasImage)
            : ClipboardImageResponse.For(asked.Id, answer.Data)).ConfigureAwait(false);
    }

    private string? ClipboardCallerRefusal(ControlRequest request) =>
        PaneCallerRefusal(request, $"clipboard image for {request.Pane ?? "an unnamed pane"}", "reads", _clipboardReads, options.ClipboardImageLimit);

    private ControlResponse CopyToViewer(ControlRequest request)
    {
        var what = $"clipboard copy from {request.Pane ?? "an unnamed pane"}";
        if (PaneCallerRefusal(request, what, "copies", _clipboardCopies, options.ClipboardCopyLimit) is { } refused)
        {
            return new ControlResponse { Ok = false, Error = refused };
        }

        if (request.Text is not { } text)
        {
            options.Log($"{what} refused: no text");
            return new ControlResponse { Ok = false, Error = "no text to copy" };
        }

        var bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes > GhosttyTerminal.MaxClipboard)
        {
            options.Log($"{what} refused: {bytes} bytes is over the {GhosttyTerminal.MaxClipboard} byte limit");
            return new ControlResponse { Ok = false, Error = $"the text is too large to copy: {bytes} bytes, at most {GhosttyTerminal.MaxClipboard}" };
        }

        lock (_gate)
        {
            if (CopyTarget(request.Pane!) is null)
            {
                options.Log($"{request.Pane} copy of {text.Length} chars dropped: no client is attached to take it");
                return new ControlResponse { Ok = false, Error = "no viewer is attached to take the copy" };
            }
        }

        options.ClipboardTrace.Write("shim", text, request.Pane);
        _copies.Enqueue((request.Pane!, text));
        _wake.Release();
        return new ControlResponse { Ok = true };
    }

    private string? PaneCallerRefusal(
        ControlRequest request, string what, string uses, Dictionary<string, Queue<DateTimeOffset>> window, ClipboardImageLimit limit)
    {
        if (request.Pane is not { Length: > 0 } pane
            || !_paneTokens.TryGetValue(pane, out var expected)
            || request.Token is not { Length: > 0 } token
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token)))
        {
            options.Log($"{what} refused: wrong or missing pane token");
            return $"the pane token of {request.Pane} is wrong or missing";
        }

        var now = options.Clock.GetUtcNow();
        lock (window)
        {
            var seen = window.TryGetValue(pane, out var queue) ? queue : window[pane] = new Queue<DateTimeOffset>();
            while (seen.TryPeek(out var oldest) && now - oldest >= limit.Per)
            {
                seen.Dequeue();
            }

            if (seen.Count >= limit.Reads)
            {
                options.Log($"{what} refused: more than {limit.Reads} {uses} in {limit.Per.TotalSeconds:0} s");
                return $"too many clipboard {uses} from {pane}: at most {limit.Reads} per {limit.Per.TotalSeconds:0} s";
            }

            seen.Enqueue(now);
        }

        return null;
    }

    public const string PasteImageCommand = "paste-image";

    private async Task PasteClipboardImageAsync(AttachSession session)
    {
        PaneRuntime? target;
        lock (_gate)
        {
            _model.Touch(session.Client);
            target = FocusedRuntime(session.Client);
        }

        if (target is null || options.DropClipboardImage is not { } drop)
        {
            return;
        }

        var answer = await AskClipboardImageAsync(session, target.Id, probe: false, ClipboardImageWithin, _stop.Token, paste: true).ConfigureAwait(false);
        if (answer.Error is not null || answer.Data is not { Length: > 0 } png)
        {
            options.Log($"paste image into {target.Id}: {answer.Error ?? "no image on the viewer clipboard"}");
            return;
        }

        if (png.Length > ClipboardImageResponse.MaxBytes || drop(png) is not { } typed)
        {
            return;
        }

        lock (_gate)
        {
            if (!_runtimes.ContainsKey(target.Id))
            {
                return;
            }
        }

        FollowOutput(target);
        target.Send(Encoding.UTF8.GetBytes(PasteBytes(new TextMessage { Text = typed, Paste = true }, target.Modes.BracketedPaste)));
    }

    private AttachSession? FocusingViewer(string pane) =>
        _sessions.Values
            .Where(s => _model.View(s.Client)?.Focused == pane)
            .OrderByDescending(s => _model.Client(s.Client)?.LastActive ?? 0)
            .FirstOrDefault();

    private async Task<ClipboardImageResponse> AskClipboardImageAsync(
        AttachSession viewer, string pane, bool probe, TimeSpan within, CancellationToken ct, bool paste = false)
    {
        var id = Interlocked.Increment(ref _nextClipboardAsk);
        var reply = new TaskCompletionSource<ClipboardImageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _clipboardAsks[id] = (viewer.Client, reply);

        try
        {
            await viewer.Wire.SendAsync(
                MessageType.ClipboardImageRequest,
                new ClipboardImageRequest { Id = id, Pane = pane, Probe = probe, Paste = paste },
                WireJsonContext.Default.ClipboardImageRequest,
                ct).ConfigureAwait(false);
            return await reply.Task.WaitAsync(within, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new ClipboardImageResponse { Id = id, Error = $"the viewer did not answer within {within.TotalSeconds:0} s" };
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return new ClipboardImageResponse { Id = id, Error = $"the viewer went away: {e.Message}" };
        }
        finally
        {
            _clipboardAsks.TryRemove(id, out _);
        }
    }

    public const string RemoteHeadOp = "remote-head";

    public const string RemoteCreateProjectOp = "remote-create-project";

    private static bool RunsForward(string op, IsoConfig iso, bool bridged) =>
        ForwardHub.SlowOps.Contains(op)
        && (!iso.On || (!bridged && op is ForwardHub.RemoveOp or ForwardHub.StackStopOp));

    private static string? IsoRefusal(string op, IsoConfig iso) =>
        !iso.On ? null
        : op is RemoteHeadOp or RemoteCreateProjectOp ? IsoProjection.Refused
        : ForwardHub.SlowOps.Contains(op) ? IsoGuard.Refusal(IsoGuard.Forward)
        : null;

    private ControlResponse ExecuteIso(ControlRequest request, string? attachedClient, IsoConfig iso)
    {
        var codes = IsoCodes.Assign(iso, options.SavedProjects(), options.Worktrees);

        if (IsoFilter.Inbound(request, codes) is { } refused)
        {
            return new ControlResponse { Id = request.Id, Ok = false, Error = refused };
        }

        var response = Execute(request, attachedClient);
        IsoFilter.Outbound(response, codes);
        return response;
    }

    private async Task AnswerViewerForwardAsync(Wire wire, int port, ControlResponse response, string reply, CancellationToken ct)
    {
        var waiting = _viewerAnswers[reply];
        try
        {
            response.ForwardResult = await waiting.Answer.Task.WaitAsync(options.Forwards.ViewerAnswerWithin, ct).ConfigureAwait(false);
            if (response.ForwardResult.State == ForwardResults.Unsupported)
            {
                options.Log($"{response.Viewer} cannot forward {port} ({response.ForwardResult.Reason}); falling back to the ssh command");
                lock (_gate)
                {
                    if (_sessions.ContainsKey(waiting.Viewer))
                    {
                        _cannotForward.Add(waiting.Viewer);
                    }
                }

                response.Viewer = null;
                response.Ssh = waiting.Fallback;
            }
        }
        catch (TimeoutException)
        {
            var skew = waiting.Build is { } build && build != FleetVersion.Current
                ? $"{response.Viewer} runs fleet {build}, this machine {FleetVersion.Current}"
                : null;
            options.Log($"{response.Viewer} did not answer forward-port {port} within {options.Forwards.ViewerAnswerWithin.TotalSeconds:0}s{(skew is null ? string.Empty : $" ({skew})")}");
            response.ForwardResult = ForwardHub.ForwardResult(
                ForwardResults.NoAnswer, $"http://localhost:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}", skew);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _viewerAnswers.TryRemove(reply, out _);
        }

        try
        {
            await wire.SendAsync(MessageType.Response, response, WireJsonContext.Default.ControlResponse, ct)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private async Task AnswerForwardAsync(Wire wire, ControlRequest request, CancellationToken ct)
    {
        ControlResponse response;
        try
        {
            response = await _hub.HandleAsync(request, LinkOf, ConnectRemoteLocked, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            response = new ControlResponse { Ok = false, Error = e.Message };
        }

        response.Id = request.Id;

        try
        {
            await wire.SendAsync(MessageType.Response, response, WireJsonContext.Default.ControlResponse, ct)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private RemoteLink? LinkOf(string host)
    {
        lock (_gate)
        {
            return _remotes.GetValueOrDefault(host);
        }
    }

    private void ConnectRemoteLocked(string host)
    {
        lock (_gate)
        {
            ConnectRemote(host);
        }
    }

    private bool IsCurrent(RemoteLink link)
    {
        lock (_gate)
        {
            return _remotes.GetValueOrDefault(link.Host) == link;
        }
    }

    private async Task RunLinkAsync(RemoteLink link)
    {
        await link.RunAsync(_stop.Token).ConfigureAwait(false);

        if (_stop.IsCancellationRequested || _hub.Ended(link, IsCurrent(link)) is not { } wait)
        {
            return;
        }

        options.Log($"remote {link.Host}: the link dropped with forwards open; reconnecting in {wait.TotalSeconds:0}s");

        try
        {
            await Task.Delay(wait, _stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            if (_remotes.GetValueOrDefault(link.Host) == link)
            {
                ConnectRemote(link.Host);
            }
        }
    }

    private IEnumerable<ForwardDto> ViewerForwards() => _viewerForwards.Values.SelectMany(v => v);

    private static ForwardDto Viewed(ForwardDto forward, string viewer) =>
        new()
        {
            Host = viewer,
            RemotePort = forward.RemotePort,
            LocalPort = forward.LocalPort,
            State = forward.State,
            Project = forward.Project,
            Error = forward.Error,
            Viewer = true,
        };

    private void OpenOnViewer(int port)
    {
        var viewer = _viewerForwards
            .Where(v => v.Value.Any(f => f.RemotePort == port && f.LocalPort is not null))
            .Select(v => v.Key)
            .FirstOrDefault(_sessions.ContainsKey)
            ?? throw new InvalidOperationException($"no machine viewing this one forwards port {port}");

        _sessions[viewer].Pending.Enqueue(new HostEffect { Kind = HostEffects.OpenUrl, Value = port.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        _wake.Release();
    }

    private (string? Viewer, string? Reply) ToViewer(string? client, string? shown, string kind, int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{port} is not a port");
        }

        if (ViewerOf(client, shown) is not { } viewer)
        {
            return (null, null);
        }

        var reply = kind == HostEffects.ForwardPort && _model.Client(viewer)?.Label is not null ? Guid.NewGuid().ToString("N") : null;
        if (reply is not null)
        {
            _viewerAnswers[reply] = new ViewerAnswer(viewer, _model.Client(viewer)?.Build);
        }

        _sessions[viewer].Pending.Enqueue(new HostEffect
        {
            Kind = kind,
            Value = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Reply = reply,
        });
        _wake.Release();
        options.Log($"{viewer}: {kind} {port} sent to the machine viewing this one");
        return (_viewerForwards.GetValueOrDefault(viewer)?.FirstOrDefault()?.Host ?? "the machine viewing this one", reply);
    }

    private string? ViewerOf(string? client, string? shown) =>
        (client ?? Showing(_sessions.Keys.Where(IsViewer), shown)) is { } viewer && IsViewer(viewer) && !_cannotForward.Contains(viewer)
        && (client is not null || TakesCallersForward(viewer, shown))
            ? viewer
            : null;

    private bool TakesCallersForward(string viewer, string? shown) =>
        !_tunnelsItself.Contains(viewer) || shown is null || _model.Client(viewer)?.Showing == shown;

    private bool IsViewer(string client) =>
        _sessions.ContainsKey(client) && (_model.Client(client)?.Label is not null || _tunnelsItself.Contains(client));

    private string? SshOf(string? client, string? shown) =>
        client is not null
            ? _sshOf.GetValueOrDefault(client)
            : Showing(_sshOf.Keys, shown) is { } showing ? _sshOf[showing] : null;

    private string? Showing(IEnumerable<string> clients, string? shown)
    {
        var candidates = clients.ToList();
        return candidates.FirstOrDefault(c => shown is not null && _model.Client(c)?.Showing == shown)
            ?? candidates.FirstOrDefault(_viewerForwards.ContainsKey)
            ?? candidates.FirstOrDefault();
    }

    private async Task AnswerHeadAsync(Wire wire, ControlRequest request, bool redact, CancellationToken ct)
    {
        var tool = request.Text ?? string.Empty;
        IReadOnlyDictionary<string, string> arguments = request.Env ?? [];
        ControlResponse response;

        try
        {
            if (request.Op == RemoteHeadOp)
            {
                RemoteLink link;
                lock (_gate)
                {
                    link = RemoteFor(request.Host);
                }

                response = link.IsConnected
                    ? await link.HeadAsync(tool, arguments).ConfigureAwait(false)
                    : new ControlResponse { Ok = false, Error = $"{request.Host} is not connected" };

                if (!response.Ok && response.Error?.StartsWith("unknown operation", StringComparison.Ordinal) == true)
                {
                    response.Error = $"the fleet on {link.Name} is too old to take head tools; update fleet there";
                }
            }
            else if (options.Head is { } head)
            {
                var (text, failed) = await head(tool, arguments, ct).ConfigureAwait(false);
                response = new ControlResponse { Ok = true, Text = text, ToolFailed = failed };
            }
            else
            {
                response = new ControlResponse { Ok = false, Error = "this fleetd cannot serve head tools" };
            }
        }
        catch (Exception e) when (redact || e is IOException or InvalidOperationException or OperationCanceledException)
        {
            response = new ControlResponse { Ok = false, Error = e.Message };
        }

        if (redact && !response.Ok)
        {
            response.Error = IsoFilter.Failed;
        }

        response.Id = request.Id;

        try
        {
            await wire.SendAsync(MessageType.Response, response, WireJsonContext.Default.ControlResponse, ct)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            options.Log($"could not answer {request.Op} {tool}: {e.Message}");
        }
    }

    private async Task AnswerCreateProjectAsync(Wire wire, ControlRequest request, bool refuse, CancellationToken ct)
    {
        var creation = ProjectCreationWire.Creation(request);
        ControlResponse response;

        try
        {
            if (refuse)
            {
                response = new ControlResponse { Ok = false, Error = IsoProjection.Refused };
            }
            else if (request.Op == RemoteCreateProjectOp)
            {
                RemoteLink link;
                lock (_gate)
                {
                    link = RemoteFor(request.Host);
                }

                response = link.IsConnected
                    ? await link.CreateProjectAsync(creation).ConfigureAwait(false)
                    : new ControlResponse { Ok = false, Error = $"{request.Host} is not connected" };

                if (!response.Ok && response.Error?.StartsWith("unknown operation", StringComparison.Ordinal) == true)
                {
                    response.Error = $"the fleet on {link.Name} is too old to create projects; update fleet there";
                }
            }
            else
            {
                var (text, failed) = await options.CreateProject!(creation, ct).ConfigureAwait(false);
                response = new ControlResponse { Ok = true, Text = text, ToolFailed = failed };
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException)
        {
            response = new ControlResponse { Ok = false, Error = e.Message };
        }

        response.Id = request.Id;

        try
        {
            await wire.SendAsync(MessageType.Response, response, WireJsonContext.Default.ControlResponse, ct)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            options.Log($"could not answer {request.Op} {creation.Name}: {e.Message}");
        }
    }

    public const string MenuWaitOp = "menu-wait";

    private readonly Dictionary<string, TaskCompletionSource<string?>> _menuWaits = new(StringComparer.Ordinal);

    private async Task AnswerMenuWaitAsync(Wire wire, ControlRequest request, CancellationToken gone, CancellationToken ct)
    {
        TaskCompletionSource<string?>? waiting = null;
        lock (_gate)
        {
            if (request.Caller is { } pane && _model.Float(pane) is { Parked: true })
            {
                waiting = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _menuWaits.Remove(pane, out var replaced);
                replaced?.TrySetCanceled();
                _menuWaits[pane] = waiting;
                options.Log($"{pane} waits to open");
            }
        }

        var response = new ControlResponse { Id = request.Id, Ok = waiting is not null };

        try
        {
            if (waiting is null)
            {
                response.Error = $"{request.Caller} is not a parked menu";
            }
            else
            {
                try
                {
                    response.Text = await waiting.Task.WaitAsync(gone).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!gone.IsCancellationRequested)
                {
                    response.Ok = false;
                    response.Error = $"{request.Caller} stopped waiting to open";
                }
                finally
                {
                    StopWaiting(request.Caller!, waiting);
                }
            }

            await wire.SendAsync(MessageType.Response, response, WireJsonContext.Default.ControlResponse, ct)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            options.Log($"could not answer {request.Op} for {request.Caller}: {e.Message}");
        }
    }

    private void StopWaiting(string pane, TaskCompletionSource<string?> waiting)
    {
        lock (_gate)
        {
            if (_menuWaits.TryGetValue(pane, out var registered) && registered == waiting)
            {
                _menuWaits.Remove(pane);
                options.Log($"{pane} no longer waits to open");
            }
        }
    }

    private void HideUntilRedrawn(FloatState box)
    {
        if (box.Hidden || !_runtimes.TryGetValue(box.Pane, out var runtime))
        {
            return;
        }

        box.Hidden = true;
        box.HiddenSince = DateTime.UtcNow;
        box.RevealAfterOutput = runtime.Outputs;
        box.Baseline = Signature(runtime.Screen);
        box.NeedsBaseline = false;
    }

    private void ForgetMenuWaits()
    {
        foreach (var pane in _menuWaits.Keys.Where(p => _model.Float(p) is not { Parked: true }).ToList())
        {
            _menuWaits.Remove(pane, out var gone);
            gone?.TrySetCanceled();
        }
    }

    private void Route(AttachSession session, KeyMessage key)
    {
        PaneRuntime? target;
        bool unhovered;
        lock (_gate)
        {
            _model.Touch(session.Client);
            target = FocusedRuntime(session.Client);
            unhovered = session.Hover is not null | DropNotice(session);
            session.Hover = null;
        }

        if (unhovered)
        {
            _wake.Release();
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

        _keyTiming?.Key(session.Client, target.Id);
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
        bool dropped;
        lock (_gate)
        {
            _model.Touch(session.Client);
            target = FocusedRuntime(session.Client);
            dropped = DropNotice(session);
        }

        if (dropped)
        {
            _wake.Release();
        }

        if (target is null)
        {
            return;
        }

        FollowOutput(target);

        if (text.Bytes is { } raw)
        {
            _keyTiming?.Key(session.Client, target.Id);
            target.Send(Convert.FromBase64String(raw));
            return;
        }

        target.Send(Encoding.UTF8.GetBytes(PasteBytes(text, target.Modes.BracketedPaste)));
    }

    private static BorderTip? HoverTip(AttachSession session, ClientView view) =>
        session.Hover is var (pane, x, y) && BorderTips.At(view, x, y) is { } tip && tip.Pane == pane ? tip : null;

    private void Unhover(string pane)
    {
        foreach (var session in _sessions.Values.Where(s => s.Hover?.Pane == pane))
        {
            session.Hover = null;
        }
    }

    private (string Pane, FloatButton Button)? FrameButton(string client, int x, int y)
    {
        if (_model.View(client) is not { Overlay: null } view
            || view.FloatingPanes.Any(f => f.Area.Contains(x, y)))
        {
            return null;
        }

        foreach (var placed in view.Panes)
        {
            if (BorderButtons.At(ClientView.Around(placed.Area), view.FrameButtonsOf(placed.Pane), x, y) is { } button)
            {
                return (placed.Pane, button);
            }
        }

        return null;
    }

    private void Mouse(AttachSession session, MouseMessage mouse)
    {
        PaneRuntime? target = null;
        (PaneRuntime Pane, string Send)? clicked = null;
        byte[]? encoded = null;
        var x = 0;
        var y = 0;
        var redraw = false;

        lock (_gate)
        {
            _model.Touch(session.Client);

            var wheel = mouse.Button >= MouseButtons.WheelUp;
            var press = mouse.Action == MouseActions.Press && !wheel;
            var hover = mouse.Action == MouseActions.Motion && !mouse.Held && session.Capture is null
                && _model.View(session.Client) is { } hovered
                && BorderTips.At(hovered, mouse.X, mouse.Y) is { } tip
                    ? (tip.Pane, mouse.X, mouse.Y)
                    : ((string Pane, int X, int Y)?)null;

            if (hover != session.Hover)
            {
                session.Hover = hover;
                redraw = true;
            }

            if (press && session.Copy is { Dragged: true } stale)
            {
                session.Copy = null;
                _runtimes.GetValueOrDefault(stale.Pane)?.Dirty = true;
                redraw = true;
            }

            if (session.Capture is { } capture && !press)
            {
                if (mouse.Action == MouseActions.Release && !mouse.Held)
                {
                    session.Capture = null;
                }

                if (capture.Selecting)
                {
                    redraw |= !wheel && DragSelection(session, capture.Pane!, mouse);
                }
                else if (capture.Kind == MouseHitKind.None)
                {
                }
                else if (capture.Kind is MouseHitKind.FloatMove or MouseHitKind.FloatResize)
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
            else if (press && mouse.Button == MouseButtons.Left && FrameButton(session.Client, mouse.X, mouse.Y) is var (owner, pill))
            {
                _model.Focus(owner);
                clicked = _runtimes.GetValueOrDefault(owner) is { } runtime ? (runtime, pill.Send) : null;
                session.Capture = new MouseCapture(owner, -1, MouseHitKind.None);
                redraw = true;
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
                        else if (_model.View(session.Client) is { } noticed
                                 && Composer.NoticeSpan(noticed) is var (from, to)
                                 && mouse.X >= from && mouse.X < to)
                        {
                            OpenMenu(session.Client, NoticesMenu);
                            redraw = true;
                        }

                        break;

                    case MouseHitKind.FloatMove or MouseHitKind.FloatResize when press && mouse.Button == MouseButtons.Left:
                        if (_model.View(session.Client) is { } floating
                            && floating.FloatingPanes.FirstOrDefault(p => p.Pane == hit.Pane) is { Pane: not null } pressed
                            && BorderButtons.At(pressed.Area, floating.ButtonsOf(pressed.Pane), mouse.X, mouse.Y) is { } button)
                        {
                            _model.Focus(pressed.Pane);
                            clicked = _runtimes.GetValueOrDefault(pressed.Pane) is { } runtime ? (runtime, button.Send) : null;
                            session.Capture = new MouseCapture(pressed.Pane, -1, MouseHitKind.None);
                            redraw = true;
                        }
                        else if (_model.View(session.Client)?.FloatingPanes.FirstOrDefault(p => p.Pane == hit.Pane) is { Pane: not null } box)
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

                            if (mouse.Button == MouseButtons.Left
                                && session.Copy is null or { Dragged: true }
                                && StartSelection(hit.Pane!, mouse, hit.X, hit.Y, out encoded) is { } selection)
                            {
                                session.Copy = selection;
                                session.Capture = session.Capture with { Selecting = true };
                                redraw = true;
                                break;
                            }
                        }

                        target = _runtimes.GetValueOrDefault(hit.Pane!);
                        (x, y) = (hit.X, hit.Y);
                        break;
                }
            }
        }

        if (clicked is var (pane, send))
        {
            Press(pane, send);
        }

        if (target is not null)
        {
            byte[] bytes;
            lock (target.Gate)
            {
                bytes = encoded ?? target.Terminal.EncodeMouse(mouse, x, y);

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

    private CopySession? StartSelection(string pane, MouseMessage mouse, int x, int y, out byte[]? encoded)
    {
        encoded = null;
        if (_runtimes.GetValueOrDefault(pane) is not { } runtime)
        {
            return null;
        }

        lock (runtime.Gate)
        {
            if ((mouse.Mods & (int)Mods.Shift) == 0)
            {
                encoded = runtime.Terminal.EncodeMouse(mouse, x, y);
                if (encoded.Length > 0)
                {
                    return null;
                }
            }

            encoded = null;
            runtime.Dirty = true;
            return CopySession.Drag(pane, new TextPoint(runtime.Terminal.Viewport.Top + y, x));
        }
    }

    private bool DragSelection(AttachSession session, string pane, MouseMessage mouse)
    {
        if (session.Copy is not { Dragged: true } copy
            || copy.Pane != pane
            || !_runtimes.TryGetValue(pane, out var runtime)
            || _model.Relative(session.Client, pane, mouse.X, mouse.Y) is not var (x, y))
        {
            return false;
        }

        lock (runtime.Gate)
        {
            copy.DragTo(new TextPoint(runtime.Terminal.Viewport.Top + y, x));
            runtime.Dirty = true;

            if (mouse.Action == MouseActions.Release && !mouse.Held)
            {
                if (copy.Selected(runtime.Terminal) is { Length: > 0 } text)
                {
                    _copies.Enqueue((pane, text));
                }
                else
                {
                    session.Copy = null;
                }
            }
        }

        return true;
    }

    private static void Press(PaneRuntime pane, string send)
    {
        if (Input.BorderKeys.Chord(send) is not { } chord)
        {
            return;
        }

        byte[] bytes;
        lock (pane.Gate)
        {
            bytes = (pane.Modes.Win32Input ? Input.BorderKeys.Win32(chord) : null)
                    ?? pane.Terminal.Encode(Input.BorderKeys.Message(chord));
        }

        if (bytes.Length > 0)
        {
            pane.Send(bytes);
        }
    }

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
        if (command.Name == "menu")
        {
            _timing?.Begin(session.Client);
        }

        if (command.Name is not ("focus-in" or "focus-out" or "copy" or "float-move" or "float-size"))
        {
            options.Log($"{session.Client}: {command.Name}{(command.Arg is null ? string.Empty : " " + command.Arg)}");
        }

        switch (command.Name)
        {
            case "smart-focus":
                SmartFocus(session, command);
                return;
            case "smart-resize":
                SmartResize(session, command);
                return;
            case "newline":
                Newline(session, command);
                return;
            case "copy" when command.Arg == Input.CopyMode.Selection:
                CopySelection(session, command);
                return;
            case PasteImageCommand:
                _ = Task.Run(() => PasteClipboardImageAsync(session), CancellationToken.None);
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
                case "head":
                    ToggleHead(session.Client, command.Arg == "voice");
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

    private static bool DropNotice(AttachSession session)
    {
        if (session.Badge is not (Input.CopyMode.NothingSelected or Input.CopyMode.SentToApp or Input.CopyMode.AppOwnsMouse))
        {
            return false;
        }

        session.Badge = null;
        return true;
    }

    private void CopySelection(AttachSession session, CommandMessage command)
    {
        PaneRuntime? app = null;
        byte[]? chord = null;

        lock (_gate)
        {
            _model.Touch(session.Client);

            if (session.Copy is { Dragged: true } kept && _runtimes.TryGetValue(kept.Pane, out var dragged))
            {
                string? text;
                lock (dragged.Gate)
                {
                    text = kept.Selected(dragged.Terminal);
                }

                if (text is { Length: > 0 })
                {
                    _copies.Enqueue((dragged.Id, text));
                }
            }
            else if (session.Copy is not null)
            {
                CopyStep(session, "yank");
            }
            else if (FocusedRuntime(session.Client) is { } focused && OwnsMouse(focused))
            {
                chord = DistinctCopyChord(focused, command);
                app = chord is null ? null : focused;
                session.Badge = chord is null ? Input.CopyMode.AppOwnsMouse : Input.CopyMode.SentToApp;
            }
            else
            {
                session.Badge = Input.CopyMode.NothingSelected;
            }
        }

        if (app is not null && chord is not null)
        {
            FollowOutput(app);
            app.Send(chord);
        }

        _wake.Release();
    }

    private static bool OwnsMouse(PaneRuntime pane)
    {
        var probe = new MouseMessage { Button = MouseButtons.Left, Action = MouseActions.Press, Held = true };

        lock (pane.Gate)
        {
            return pane.Terminal.EncodeMouse(probe, 0, 0).Length > 0;
        }
    }

    private static byte[]? DistinctCopyChord(PaneRuntime pane, CommandMessage command)
    {
        if (pane.Modes.Win32Input || CopyChord(command) is not { } key)
        {
            return null;
        }

        byte[] bytes;
        lock (pane.Gate)
        {
            bytes = pane.Terminal.Encode(key);
        }

        return bytes.Length > 0 && bytes.All(b => b is (>= 0x20 and not 0x7f) or 0x1b) ? bytes : null;
    }

    private static KeyMessage? CopyChord(CommandMessage command)
    {
        if (command.Key is { } key)
        {
            return new KeyMessage { Key = key.Key, Mods = key.Mods, Consumed = key.Consumed, Text = key.Text, Unshifted = key.Unshifted };
        }

        return command.Bytes is { } raw
               && Input.ModifiedKeys.Parse(Input.ModifiedKeys.Normalize(Convert.FromBase64String(raw))) is var (modifier, code, _)
               && code is 'c' or 'C'
               && Input.ModifiedKeys.IsClipboardChord(modifier, code)
            ? new KeyMessage { Key = (int)Key.C, Mods = (int)(Mods.Ctrl | Mods.Shift), Unshifted = 'c' }
            : null;
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
        UnlessNvim(session, command, () => _model.FocusDirection(session.Client, dx, dy));
    }

    private void SmartResize(AttachSession session, CommandMessage command) =>
        UnlessNvim(
            session,
            command,
            () => _model.ResizeFocused(session.Client, command.Arg ?? string.Empty, ResizeCells));

    private void UnlessNvim(AttachSession session, CommandMessage command, Action own)
    {
        bool nvim;

        lock (_gate)
        {
            _model.Touch(session.Client);
            nvim = _model.View(session.Client)?.Focused is { } focused && _model.IsNvim(focused);

            if (!nvim)
            {
                own();
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

    private void ConnectRemote(string host)
    {
        if (options.RemoteOpen is not { } open)
        {
            throw new InvalidOperationException("this fleetd cannot connect to remotes");
        }

        if (options.Iso().On)
        {
            throw new InvalidOperationException(IsoGuard.Refusal(IsoGuard.Ssh));
        }

        if (_remotes.TryGetValue(host, out var existing) && existing.Snapshot().State != RemoteLink.Failed)
        {
            return;
        }

        var link = new RemoteLink(host, token => open(host, token), options.Log) { CanForward = _hub.For(host) is not null };
        link.Effect += effect => Forward(link, effect);
        link.Noticed += fresh => Noticed(link, fresh);
        link.ClipboardImageRequested += asked => _ = Task.Run(() => RelayClipboardImageAsync(link, asked), CancellationToken.None);
        _remotes[host] = link;
        _hub.Watch(link, IsCurrent, _stop.Token);
        _ = Task.Run(() => RunLinkAsync(link), CancellationToken.None);
        options.Log($"remote {host}: connecting");
    }

    public const string LocalCommands = "|switch-project|next-workspace|show|redraw|head|";

    public static string RemoteWorkspace(string machine) => "@" + machine;

    private RemoteLink? RemoteTarget(AttachSession session, MessageType type, byte[] payload)
    {
        lock (_gate)
        {
            var runtime = type == MessageType.Mouse
                ? Wire.Read(payload, WireJsonContext.Default.MouseMessage) is var mouse
                  && _model.Hit(session.Client, mouse.X, mouse.Y) is { Kind: MouseHitKind.Pane, Pane: { } under }
                    ? _runtimes.GetValueOrDefault(under)
                    : null
                : FocusedRuntime(session.Client);

            if (runtime?.Pty is not RemotePty pty
                || _remotes.Values.FirstOrDefault(r => r.Pty == pty) is not { } link)
            {
                return null;
            }

            if (type == MessageType.Command
                && LocalCommands.Contains($"|{Wire.Read(payload, WireJsonContext.Default.CommandMessage).Name}|", StringComparison.Ordinal))
            {
                return null;
            }

            _model.Touch(session.Client);
            return link;
        }
    }

    private double ShowRemote(string? client, string? host, string? project)
    {
        var link = RemoteFor(host);
        if (client is null || project is null || !link.IsConnected)
        {
            throw new InvalidOperationException($"{host} is not connected");
        }

        var shown = ShowViewer(client, link);
        _ = Task.Run(async () =>
        {
            try
            {
                await link.ShowAsync(project).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
            {
                options.Log($"remote {link.Host}: could not show {project}: {e.Message}");
            }
        });

        return shown;
    }

    private double NewRemoteProject(string? client, string? host)
    {
        var link = RemoteFor(host);
        if (client is null || !link.IsConnected)
        {
            throw new InvalidOperationException($"{host} is not connected");
        }

        var shown = ShowViewer(client, link);
        _ = Task.Run(async () =>
        {
            try
            {
                await link.NewProjectAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException)
            {
                options.Log($"remote {link.Host}: could not start a new project: {e.Message}");
            }
        });

        return shown;
    }

    private double ShowViewer(string client, RemoteLink link)
    {
        var workspace = RemoteWorkspace(link.Name);
        if (_model.Workspace(workspace) is not { } existing || existing.RemoteHost != link.Host
            || existing.Tabs.SelectMany(t => t.Root.Panes()).All(p => _runtimes.GetValueOrDefault(p)?.Pty != link.Pty))
        {
            foreach (var stale in _model.PanesIn(workspace).ToList())
            {
                Kill(stale);
            }

            var pane = _model.Spawn(workspace, string.Empty, ["remote", link.Host]);
            _model.Workspace(workspace)!.RemoteHost = link.Host;
            ApplyResizes();
            Start(pane, null, link.Pty);
        }

        return Show(client, workspace);
    }

    public const string HandedBack = "switch-project";

    public static bool HandsBack(string? action) => action is HandedBack or NoticesMenu;


    private List<WindowEntryDto> Window(string? client)
    {
        if (_model.Client(client ?? string.Empty) is not { } c)
        {
            throw new InvalidOperationException("window needs a client");
        }

        var entries = new List<WindowEntryDto>();

        foreach (var name in c.Projects.Append(c.Showing).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var shown = string.Equals(name, c.Showing, StringComparison.OrdinalIgnoreCase);

            if (_model.Workspace(name) is not { } workspace || FleetWorkspaces.IsHidden(name))
            {
                continue;
            }

            if (workspace.RemoteHost is not { } host)
            {
                entries.Add(new WindowEntryDto { Name = name, Shown = shown });
            }
            else if (_remotes.GetValueOrDefault(host)?.Showing is { } remoteProject)
            {
                entries.Add(new WindowEntryDto { Name = remoteProject, Host = host, Shown = shown });
            }
        }

        return entries;
    }

    private void Furnish(string client, Hello hello)
    {
        foreach (var entry in hello.Window ?? [])
        {
            try
            {
                if (entry.Host is { } host)
                {
                    ShowRemote(client, host, entry.Name);
                }
                else if (_model.Workspace(entry.Name) is not null)
                {
                    _model.Show(client, entry.Name);
                }
            }
            catch (InvalidOperationException e)
            {
                options.Log($"{client}: could not add {entry.Name}{(entry.Host is null ? string.Empty : " on " + entry.Host)}: {e.Message}");
            }
        }

        if (hello.Showing is { } showing)
        {
            var target = showing.Host is { } host && _remotes.GetValueOrDefault(host) is { } link
                ? RemoteWorkspace(link.Name)
                : showing.Name;

            if (_model.Workspace(target) is not null)
            {
                _model.Show(client, target);
            }
        }
    }

    private bool HandBack(string? client, string? action)
    {
        if (!HandsBack(action) || client is null || _model.Client(client)?.Label is null
            || !_sessions.TryGetValue(client, out var viewer))
        {
            return false;
        }

        viewer.Pending.Enqueue(new HostEffect { Kind = HostEffects.HandBack, Value = action });
        options.Log($"{client}: {action} handed back to the machine viewing it");
        return true;
    }

    private List<NoticeDto> RemoteNotices(string? client)
    {
        if (_model.Client(client ?? string.Empty) is not { } c)
        {
            return [];
        }

        var notices = new List<NoticeDto>();
        foreach (var link in _remotes.Values)
        {
            if (link.Showing is not { } project || !MuxModel.InWindow(c, RemoteWorkspace(link.Name)))
            {
                continue;
            }

            notices.AddRange(link.Notices
                .Where(n => string.Equals(n.Project, project, StringComparison.OrdinalIgnoreCase))
                .Select(n => new NoticeDto
                {
                    Project = n.Project,
                    Host = link.Host,
                    Machine = link.Name,
                    Key = n.Key,
                    Kind = n.Kind,
                    Worktree = n.Worktree,
                    Agent = n.Agent,
                    Message = n.Message,
                    Since = n.Since,
                    Resolved = n.Resolved,
                    Dismissed = n.Dismissed,
                }));
        }

        return notices;
    }

    private void Noticed(RemoteLink link, IReadOnlyList<NoticeDto> fresh)
    {
        var alerts = new List<AttachSession>();
        IReadOnlyList<NoticeDto> heard = [];

        lock (_gate)
        {
            var workspace = RemoteWorkspace(link.Name);
            var project = link.Showing;
            _model.SetNotices(workspace, project is null ? 0 : link.Notices.Count(n => n.IsOpen && string.Equals(n.Project, project, StringComparison.OrdinalIgnoreCase)));

            heard = [.. fresh.Where(n => string.Equals(n.Project, project, StringComparison.OrdinalIgnoreCase))];
            if (heard.Count > 0)
            {
                alerts.AddRange(_sessions.Values.Where(s => _model.Client(s.Client) is { } c && MuxModel.InWindow(c, workspace)));
            }
        }

        if (alerts.Count > 0)
        {
            var (bell, toast) = options.AlertSettings();
            if (bell)
            {
                foreach (var session in alerts)
                {
                    session.Pending.Enqueue(new HostEffect { Kind = HostEffects.Bell });
                }
            }

            if (toast)
            {
                options.Toast($"fleet · {heard[0].Project} @{link.Name}", string.Join('\n', heard.Take(3).Select(n => $"{n.Agent}: {n.Message}")));
            }
        }

        _wake.Release();
    }

    private void Forward(RemoteLink link, HostEffect effect)
    {
        if (effect.Kind == MenuTiming.ReportEffect)
        {
            if (_timing is not null && effect.Value is { } report)
            {
                lock (_gate)
                {
                    _timing.Reported(link.Host, report, RemoteOutputs(link.Host));
                }
            }

            return;
        }

        if (effect.Kind == HostEffects.OpenUrl)
        {
            _ = Task.Run(() => _hub.Open(link, effect.Value), CancellationToken.None);
            return;
        }

        if (effect.Kind is HostEffects.ForwardPort or HostEffects.UnforwardPort)
        {
            _ = Task.Run(() => ForwardForRemoteAsync(link, effect), CancellationToken.None);
            return;
        }

        if (effect.Kind is not (HostEffects.Clipboard or HostEffects.Bell or HostEffects.HandBack or HostEffects.OpenWindow))
        {
            options.Log($"remote {link.Host}: ignored the {effect.Kind} effect, which fleetd {FleetVersion.Current} here does not handle");
            if (effect.Reply is { } reply)
            {
                var unsupported = ForwardHub.ForwardResult(
                    ForwardResults.Unsupported,
                    $"http://localhost:{effect.Value}",
                    $"{Environment.MachineName} runs fleetd {FleetVersion.Current}, which does not handle {effect.Kind}");
                _ = Task.Run(() => ReplyToRemoteAsync(link, effect, reply, unsupported), CancellationToken.None);
            }

            return;
        }

        lock (_gate)
        {
            var workspace = RemoteWorkspace(link.Name);
            var viewers = _sessions.Values.Where(s => _model.Client(s.Client)?.Showing == workspace).ToList();
            if (effect.Kind == HostEffects.Clipboard && viewers.Count == 0)
            {
                var chars = effect.Value?.Length ?? 0;
                if (_sessions.Values.Where(s => s.Welcomed).OrderByDescending(s => _model.Client(s.Client)?.LastActive ?? 0).FirstOrDefault() is { } latest)
                {
                    viewers.Add(latest);
                    options.Log($"remote {link.Host}: copy of {chars} chars rerouted to {latest.Client}: no window shows {workspace}");
                }
                else
                {
                    options.Log($"remote {link.Host}: copy of {chars} chars dropped: no local window to take it");
                }
            }

            if (effect.Kind == HostEffects.Clipboard)
            {
                options.ClipboardTrace.Write(
                    viewers.Count > 0 ? "remote-copy" : "remote-copy-dropped",
                    effect.Value,
                    detail: $"{link.Host} to {(viewers.Count > 0 ? string.Join(",", viewers.Select(v => v.Client)) : "nobody")}");
            }

            foreach (var session in viewers)
            {
                if (effect.Kind == HostEffects.HandBack)
                {
                    if (HandsBack(effect.Value))
                    {
                        OpenMenu(session.Client, effect.Value);
                    }

                    continue;
                }

                if (effect.Kind == HostEffects.OpenWindow)
                {
                    session.Pending.Enqueue(new HostEffect { Kind = HostEffects.OpenRemote, Value = $"{link.Host}\n{effect.Value}" });
                    continue;
                }

                session.Pending.Enqueue(effect);
            }
        }

        _wake.Release();
    }

    private async Task ForwardForRemoteAsync(RemoteLink link, HostEffect effect)
    {
        ForwardResultDto result;
        try
        {
            if (effect.Kind == HostEffects.UnforwardPort)
            {
                await _hub.UnforwardForViewerAsync(link, effect.Value, _stop.Token).ConfigureAwait(false);
                return;
            }

            result = await _hub.ForwardForViewerAsync(link, effect.Value, _stop.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            options.Log($"remote {link.Host}: could not {effect.Kind} {effect.Value}: {e.GetType().Name}: {e.Message}");
            result = ForwardHub.ForwardResult(ForwardResults.Failed, $"http://localhost:{effect.Value}", e.Message);
        }

        if (effect.Reply is { } reply)
        {
            await ReplyToRemoteAsync(link, effect, reply, result).ConfigureAwait(false);
        }
    }

    private async Task ReplyToRemoteAsync(RemoteLink link, HostEffect effect, string reply, ForwardResultDto result)
    {
        try
        {
            await link.SendAsync(new ControlRequest { Op = ForwardHub.ViewerForwardedOp, Reply = reply, ForwardResult = result })
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            options.Log($"remote {link.Host}: could not report the forward of {effect.Value}: {e.Message}");
        }
    }

    private bool MenuShown(string pane) =>
        _model.Pane(pane) is not null && _model.Float(pane) is not ({ Hidden: true } or { Parked: true });

    private long RemoteOutputs(string host) =>
        _remotes.GetValueOrDefault(host) is { } link && _runtimes.Values.FirstOrDefault(r => r.Pty == link.Pty) is { } runtime
            ? runtime.Outputs
            : 0;

    private RemoteLink RemoteFor(string? host) =>
        host is not null && _remotes.TryGetValue(host, out var link)
            ? link
            : throw new InvalidOperationException($"no remote {host}");

    private void OpenRemoteWindow(string? client, string? host, string? project)
    {
        if (client is null || host is null || project is null || !_sessions.TryGetValue(client, out var session))
        {
            throw new InvalidOperationException("open-remote-window needs a client, a host and a project");
        }

        session.Pending.Enqueue(new HostEffect { Kind = HostEffects.OpenRemote, Value = $"{host}\n{project}" });
        options.Log($"{client} opens {project} on {host} in a new window");
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

    public const string NoticesMenu = "notifications";

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

    private TimeSpan QuietFor(string pane)
    {
        var workspace = CallerWorkspace(pane);
        var viewers = _model.Clients
            .Where(c => workspace is not null && string.Equals(c.Showing, workspace, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return viewers.Count > 0 && viewers.All(c => _bridged.ContainsKey(c.Id))
            ? options.RevealWhenQuietRemote
            : options.RevealWhenQuiet;
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
                          && Environment.TickCount64 - runtime.LastOutputAt >= QuietFor(box.Pane).TotalMilliseconds;

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
                        && Environment.TickCount64 - runtime.LastOutputAt >= QuietFor(box.Pane).TotalMilliseconds;

            if (drawn || DateTime.UtcNow - box.HiddenSince > RevealAnyway)
            {
                box.Hidden = false;
                options.Log($"{box.Pane} revealed {(drawn ? "after drawing" : "after waiting")}");
                _timing?.Revealed(box.Pane);
            }
            else
            {
                _revealing = true;
            }
        }
    }


    private readonly record struct Warmed(DateTime Wall, DateTimeOffset AgainFrom);

    private readonly Dictionary<string, Warmed> _warmed = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan WarmAgainAfter = TimeSpan.FromSeconds(2);

    private List<string> MenuArgs(string? project) =>
        project is null ? [options.FleetExecutable, "menu"] : [options.FleetExecutable, "menu", "--project", project];

    private string? ShownMenuWorkspace(string? pane) =>
        pane is not null && _model.Pane(pane)?.Args is [_, "menu", ..] && _model.Float(pane) is { Parked: false }
            ? CallerWorkspace(pane)
            : null;

    private bool HasMenu(string workspace) =>
        _model.PanesIn(workspace).Any(id => _model.Pane(id)?.Args is [_, "menu", ..]);

    private bool WarmedBeforeUpdate(string workspace)
    {
        try
        {
            return _warmed.TryGetValue(workspace, out var warmed)
                && File.Exists(options.FleetExecutable)
                && File.GetLastWriteTimeUtc(options.FleetExecutable) > warmed.Wall;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WarmMenus()
    {
        ForgetMenuWaits();

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
                || (_warmed.TryGetValue(workspace, out var warmed) && options.Clock.GetUtcNow() - warmed.AgainFrom < WarmAgainAfter))
            {
                continue;
            }

            _warmed[workspace] = new Warmed(DateTime.UtcNow, options.Clock.GetUtcNow());
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

            if (_model.Parked(shown) is { } outdated && WarmedBeforeUpdate(shown))
            {
                Kill(outdated.Pane);
                options.Log($"{client}: warm menu {outdated.Pane} predates the installed fleet; opening a fresh one");
            }

            if (_model.Parked(shown) is { } warm
                && (action is null || _menuWaits.ContainsKey(warm.Pane))
                && _model.Unpark(warm.Pane))
            {
                if (action is not null)
                {
                    HideUntilRedrawn(warm);
                }

                if (_menuWaits.Remove(warm.Pane, out var waiting))
                {
                    waiting.TrySetResult(action);
                }

                state.Menu = warm.Pane;
                _timing?.Opened(client, warm.Pane, warm: true);
                options.Log($"{client}: warm menu {warm.Pane}{(action is null ? string.Empty : $" for {action}")}{(warm.Hidden ? " (still drawing)" : string.Empty)}");
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
            _timing?.Opened(client, menu.Id, warm: false);
            ApplyResizes();
            Start(menu, env);
            return;
        }

        var pane = _model.Spawn(MuxModel.OverlayWorkspace, Environment.CurrentDirectory, args);
        _model.SetOverlay(client, pane.Id);
        _timing?.Opened(client, pane.Id, warm: false);
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
            ?? request.Session
            ?? (request.NewWindow ? null : request.Window)
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
                below is { } area ? MuxModel.Over(area, _model.WindowArea(home)) : null,
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

    private void ToggleHead(string client, bool voice)
    {
        var toggled = _model.ToggleHead(client, voice);

        if (toggled is not (HeadToggle.Missing or HeadToggle.OtherMode)
            || _model.View(client) is not { Workspace: { } workspace })
        {
            return;
        }

        if (toggled == HeadToggle.OtherMode)
        {
            Kill(_model.HeadPane()?.Id);
        }

        IReadOnlyList<string> args = voice
            ? [options.FleetExecutable, MuxModel.HeadVerb, MuxModel.HeadVoiceFlag]
            : [options.FleetExecutable, MuxModel.HeadVerb];

        var created = _model.SpawnHead(
            workspace.Name, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), args);
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

    private bool ForRemoteViewer(IReadOnlyDictionary<string, string>? requested) =>
        requested is not null && requested.TryGetValue(AgentHarness.RemotePaneVariable, out var marked)
            ? marked == "1"
            : !_bridged.IsEmpty;

    private void MarkBridged(bool attached)
    {
        if (options.BridgedFile is not { } file)
        {
            return;
        }

        try
        {
            if (attached)
            {
                File.WriteAllText(file, string.Empty);
            }
            else
            {
                File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            options.Log($"cannot {(attached ? "write" : "remove")} {file}: {e.Message}");
        }
    }

    private void Start(PaneState pane, IReadOnlyDictionary<string, string>? requested, IPanePty? given = null)
    {
        var kept = (requested ?? new Dictionary<string, string>())
            .Where(e => e.Key != ClientVariable)
            .ToDictionary(e => e.Key, e => e.Value);
        if (ServesRemoteViewers && ForRemoteViewer(requested))
        {
            AgentHarness.MarkRemotePane(kept);
        }
        else
        {
            kept.Remove(AgentHarness.RemotePaneVariable);
        }

        pane.Env = kept;

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ProfileEnv(pane.Cwd))
        {
            env[key] = value;
        }

        foreach (var (key, value) in requested ?? new Dictionary<string, string>())
        {
            env[key] = value;
        }

        if (ServesRemoteViewers)
        {
            options.RemotePaneEnv?.Invoke(env);
        }

        env.Remove(AgentHarness.RemotePaneVariable);
        if (ServesRemoteViewers && kept.ContainsKey(AgentHarness.RemotePaneVariable))
        {
            AgentHarness.MarkRemotePane(env);
        }

        if (options.BridgedFile is { } bridgedFile)
        {
            env[AgentHarness.BridgedFileVariable] = bridgedFile;
        }

        env = new Dictionary<string, string>(env, StringComparer.OrdinalIgnoreCase)
        {
            ["FLEET_MUX"] = "embedded",
            [PaneVariable] = pane.Id,
            [Endpoint.Variable] = options.Endpoint.Address,
            ["WEZTERM_PANE"] = string.Empty,
            [ExecutableVariable] = options.FleetExecutable,
            [FloatPane.Variable] = _model.FloatBounds(pane.Id) is not null || _model.IsOverlay(pane.Id) ? "1" : string.Empty,
            [FramedPane.Variable] = MuxModel.IsDashboard(pane) ? "1" : string.Empty,
            ["WEZTERM_UNIX_SOCKET"] = string.Empty,
            ["TMUX"] = string.Empty,
        };

        if (ServesRemoteViewers)
        {
            env[PaneTokenVariable] = _paneTokens.GetOrAdd(
                pane.Id, _ => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
        }

        var cols = Math.Max(pane.Cols, 2);
        var rows = Math.Max(pane.Rows, 2);
        var pty = given ?? options.Pty();
        var runtime = new PaneRuntime(pane.Id, pty, options.Terminal, cols, rows);
        _runtimes[pane.Id] = runtime;

        runtime.Terminal.TitleChanged += () =>
        {
            runtime.TitleDirty = true;
            _wake.Release();
        };

        runtime.Terminal.Copied += text =>
        {
            options.ClipboardTrace.Write("pane-osc52", text, pane.Id);
            _copies.Enqueue((pane.Id, text));
            _wake.Release();
        };

        runtime.Terminal.CopyDropped += reason =>
        {
            options.Log($"{pane.Id} osc 52 copy dropped: {reason}");
            options.ClipboardTrace.Write("pane-osc52-dropped", null, pane.Id, reason);
        };

        pty.Output += (buffer, count) =>
        {
            _keyTiming?.Output(pane.Id);
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
                        var doomed = pty is RemotePty && CallerWorkspace(pane.Id) is { } remote
                            ? [.. _model.PanesIn(remote)]
                            : new List<string> { pane.Id };

                        foreach (var id in doomed)
                        {
                            Kill(id);
                        }

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
        var closedMenuIn = ShownMenuWorkspace(id);

        if (id is null || !_model.Kill(id))
        {
            return false;
        }

        if (closedMenuIn is not null)
        {
            if (_warmed.TryGetValue(closedMenuIn, out var warmed))
            {
                _warmed[closedMenuIn] = warmed with { AgainFrom = DateTimeOffset.MinValue };
            }
        }

        _paneTokens.TryRemove(id, out _);
        lock (_clipboardReads)
        {
            _clipboardReads.Remove(id);
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
            if (_bridged.TryRemove(session.Client, out _) && _bridged.IsEmpty)
            {
                MarkBridged(false);
            }

            _viewerForwards.Remove(session.Client);
            _sshOf.Remove(session.Client);
            _cannotForward.Remove(session.Client);
            _tunnelsItself.Remove(session.Client);
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
                        options.ClipboardTrace.Write("copy-target", copy.Text, copy.Pane, target.Client);
                    }
                    else
                    {
                        options.Log($"{copy.Pane} copy of {copy.Text.Length} chars dropped: no client is attached to take it");
                        options.ClipboardTrace.Write("copy-dropped", copy.Text, copy.Pane, "no client is attached to take it");
                    }
                }

                foreach (var session in _sessions.Values.Where(s => s.Welcomed))
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

                foreach (var session in _sessions.Values.Where(s => s.Welcomed))
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
                        view, id => _runtimes.GetValueOrDefault(id)?.Screen, session.Badge, copying, session.WhichKey, HoverTip(session, view));
                    if (session.Shown is not null && Same(session.Shown, frame))
                    {
                        _keyTiming?.Unchanged(session.Client, pane => _model.Float(pane) is { Held: not null });
                        continue;
                    }

                    var full = session.Shown is null
                        || session.Shown.Cols != frame.Cols
                        || session.Shown.Rows != frame.Rows;
                    sends.Add((session, ++session.Seq, full, FrameEncoder.Encode(session.Shown, frame), session.Switching));
                    session.Shown = frame;
                    session.Switching = null;

                    foreach (var keyed in _keyTiming?.Framed(session.Client) ?? [])
                    {
                        options.Log(keyed.Line);
                    }

                    if (_timing?.Framed(session.Client, MenuShown, RemoteOutputs) is { } timed)
                    {
                        options.Log(timed.Line);
                        if (timed.Host is null)
                        {
                            effects.Add((session, new HostEffect { Kind = MenuTiming.ReportEffect, Value = timed.Report() }));
                        }
                    }
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

            if (persisted is not null && !_forgotten)
            {
                SaveSession(persisted);
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
        options.SessionFile is not null && !_forgotten && DateTime.UtcNow - _lastSave >= options.SaveEvery;

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
        lock (_saveGate)
        {
            SaveSessionLocked(json);
        }
    }

    private void SaveSessionLocked(string json)
    {
        if (json == _savedSession || options.SessionFile is not { } file || (_forgotten && json.Length > 0))
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
        _hub.Shutdown();
        MarkBridged(false);
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

        public (string Pane, int X, int Y)? Hover { get; set; }

        public string? Title { get; set; }

        public CopySession? Copy { get; set; }

        public List<WhichKeyEntry>? WhichKey { get; set; }

        public bool SaidBye { get; set; }

        public volatile bool Welcomed;

        public System.Collections.Concurrent.ConcurrentQueue<HostEffect> Pending { get; } = new();
    }

    private sealed record MouseCapture(
        string? Pane,
        int Divider,
        MouseHitKind Kind = MouseHitKind.Pane,
        Rect Start = default,
        int FromX = 0,
        int FromY = 0,
        bool Selecting = false);
}
