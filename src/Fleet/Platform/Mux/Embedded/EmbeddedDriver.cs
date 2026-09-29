using System.Collections.Concurrent;
using System.Diagnostics;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;

namespace Fleet.Platform.Mux.Embedded;

public sealed class EmbeddedDriver(
    Endpoint endpoint, Func<Task<bool>>? startDaemon = null, string? client = null, Func<CancellationToken, Task<Stream?>>? open = null)
    : IMuxDriver, IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ControlResponse>> _pending = new();
    private Wire? _wire;
    private int _nextId;

    public string Name => "embedded";

    public MuxCaps Caps => MuxCaps.Split | MuxCaps.Detach | MuxCaps.Persist | MuxCaps.Workspaces | MuxCaps.Popup;

    public PaneId CurrentPane =>
        Environment.GetEnvironmentVariable(FleetDaemon.PaneVariable) is { Length: > 0 } pane
            ? new PaneId(pane)
            : PaneId.None;

    public string? CurrentClient =>
        client ?? (Environment.GetEnvironmentVariable(FleetDaemon.ClientVariable) is { Length: > 0 } fromEnv
            ? fromEnv
            : null);

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            return (await RequestAsync(new ControlRequest { Op = "ping" }, ct).ConfigureAwait(false)).Ok;
        }
        catch (MuxUnavailableException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default)
    {
        var response = await RequestAsync(new ControlRequest { Op = "list-panes" }, ct).ConfigureAwait(false);

        return (response.Panes ?? [])
            .Select(p => new Pane(new PaneId(p.Id), p.Window, p.Tab, p.Session, p.Title, p.Cwd, p.Active, p.PaneTitle))
            .ToList();
    }

    public async Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
    {
        var response = await RequestAsync(
            new ControlRequest
            {
                Op = "spawn",
                Workspace = options.Workspace,
                Session = options.SessionName,
                Window = options.WindowId,
                NewWindow = options.NewWindow,
                Cwd = options.Cwd,
                Args = [.. options.Args],
                Env = new Dictionary<string, string>(options.Env),
            },
            ct).ConfigureAwait(false);

        return new PaneId(response.Pane ?? string.Empty);
    }

    public async Task<PaneId> SpawnFloatingAsync(PaneId over, SpawnOptions options, CancellationToken ct = default)
    {
        var response = await RequestAsync(
            new ControlRequest
            {
                Op = "spawn-float",
                Pane = over.IsNone ? null : over.Value,
                Workspace = options.Workspace,
                Session = options.SessionName,
                Cwd = options.Cwd,
                Args = [.. options.Args],
                Env = new Dictionary<string, string>(options.Env),
            },
            ct).ConfigureAwait(false);

        return new PaneId(response.Pane ?? string.Empty);
    }

    public async Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default)
    {
        var response = await RequestAsync(
            new ControlRequest
            {
                Op = "split",
                Pane = options.Source.Value,
                Direction = options.Direction.ToString().ToLowerInvariant(),
                Percent = options.Percent,
                Cwd = options.Cwd,
                Args = [.. options.Args],
                MovePane = options.MovePane.IsNone ? null : options.MovePane.Value,
            },
            ct).ConfigureAwait(false);

        return new PaneId(response.Pane ?? string.Empty);
    }

    public Task KillPaneAsync(PaneId id, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "kill", Pane = id.Value }, ct);

    public Task MovePaneAsync(PaneId id, MovePaneOptions options, CancellationToken ct = default) =>
        RequestAsync(
            new ControlRequest
            {
                Op = "move",
                Pane = id.Value,
                Workspace = options.Workspace,
                Window = options.WindowId,
                NewWindow = options.NewWindow,
            },
            ct);

    public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "title", Pane = id.Value, Text = title }, ct);

    public Task FocusPaneAsync(PaneId id, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "focus", Pane = id.Value }, ct);

    public Task SendTextAsync(PaneId id, string text, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "send-text", Pane = id.Value, Text = text }, ct);

    public async Task<string> GetTextAsync(PaneId id, CancellationToken ct = default) =>
        (await RequestAsync(new ControlRequest { Op = "get-text", Pane = id.Value }, ct).ConfigureAwait(false)).Text
        ?? string.Empty;

    public async Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        var response = await RequestAsync(
            new ControlRequest { Op = "list-workspaces", Client = CurrentClient }, ct).ConfigureAwait(false);

        return (response.Workspaces ?? []).Select(w => new Workspace(w.Name, w.ShownHere, w.InWindow, w.InOtherWindow)).ToList();
    }

    public async Task ShowWorkspaceAsync(string name, CancellationToken ct = default)
    {
        if (CurrentClient is null && CurrentPane.IsNone)
        {
            throw new MuxUnavailableException(
                "no attached client to show a workspace in; run this from inside 'fleet attach'");
        }

        await RequestAsync(new ControlRequest { Op = "show", Client = CurrentClient, Workspace = name }, ct)
            .ConfigureAwait(false);
    }

    public async Task<(int Cols, int Rows)?> FitAsync(int cols, int rows, CancellationToken ct = default)
    {
        var fitted = await RequestAsync(new ControlRequest { Op = "fit", Cols = cols, Rows = rows }, ct).ConfigureAwait(false);
        return fitted.Cols > 0 && fitted.Rows > 0 ? (fitted.Cols, fitted.Rows) : null;
    }

    public async Task<DaemonStatusDto?> StatusAsync(CancellationToken ct = default) =>
        (await RequestAsync(new ControlRequest { Op = "status" }, ct).ConfigureAwait(false)).Status;

    public Task HoldAsync(CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "hold" }, ct);

    public Task OpenMenuAsync(string? action, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "menu", Text = action }, ct);

    public Task FocusFromAsync(string direction, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "focus-from", Direction = direction }, ct);

    public Task OpenWindowAsync(string name, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "open-window", Workspace = name, Client = CurrentClient }, ct);

    public Task CloseWorkspaceAsync(string name, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "close-workspace", Workspace = name }, ct);

    public async Task<IReadOnlyList<RemoteDto>> RemotesAsync(CancellationToken ct = default) =>
        (await RequestAsync(new ControlRequest { Op = "list-remotes" }, ct).ConfigureAwait(false)).Remotes ?? [];

    public Task ConnectRemoteAsync(string host, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "remote-connect", Host = host }, ct);

    public Task AnswerRemoteAsync(string host, string answer, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "remote-answer", Host = host, Answer = answer }, ct);

    public Task DisconnectRemoteAsync(string host, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "remote-disconnect", Host = host }, ct);

    public async Task<IReadOnlyList<WindowEntryDto>> WindowAsync(CancellationToken ct = default) =>
        (await RequestAsync(new ControlRequest { Op = "window", Client = CurrentClient }, ct).ConfigureAwait(false)).Window ?? [];

    public async Task<bool> HandBackAsync(string action, CancellationToken ct = default) =>
        (await RequestAsync(new ControlRequest { Op = "hand-back", Text = action, Client = CurrentClient }, ct).ConfigureAwait(false)).Pending;

    public Task ShowRemoteAsync(string host, string project, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "show-remote", Host = host, Workspace = project, Client = CurrentClient }, ct);

    public Task OpenRemoteWindowAsync(string host, string project, CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "open-remote-window", Host = host, Workspace = project, Client = CurrentClient }, ct);

    public async Task<(bool Pending, string? Answer)> AskPassAsync(string token, string prompt, CancellationToken ct = default)
    {
        var response = await RequestAsync(new ControlRequest { Op = "askpass", Session = token, Text = prompt }, ct).ConfigureAwait(false);
        return (response.Pending, response.Text);
    }

    public Task ShutdownAsync(CancellationToken ct = default) =>
        RequestAsync(new ControlRequest { Op = "shutdown" }, ct);

    public async Task<bool> NoticesAsync(string workspace, int open, bool bell, CancellationToken ct = default) =>
        (await RequestAsync(new ControlRequest { Op = "notices", Workspace = workspace, Count = open, Bell = bell }, ct).ConfigureAwait(false)).Pending;

    public void Dispose()
    {
        _wire?.Dispose();
        _connectGate.Dispose();
    }

    private async Task<ControlResponse> RequestAsync(ControlRequest request, CancellationToken ct)
    {
        var wire = await ConnectAsync(ct).ConfigureAwait(false);
        request.Id = Interlocked.Increment(ref _nextId);
        request.Caller ??= CurrentPane.IsNone ? null : CurrentPane.Value;

        var reply = new TaskCompletionSource<ControlResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = reply;

        try
        {
            await wire.SendAsync(MessageType.Request, request, WireJsonContext.Default.ControlRequest, ct)
                .ConfigureAwait(false);
            var response = await reply.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

            if (!response.Ok)
            {
                throw new MuxUnavailableException($"fleetd: {response.Error}");
            }

            return response;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or TimeoutException)
        {
            _wire = null;
            throw new MuxUnavailableException($"fleetd did not answer: {e.Message}");
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    private async Task<Wire> ConnectAsync(CancellationToken ct)
    {
        if (_wire is { } existing)
        {
            return existing;
        }

        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_wire is { } raced)
            {
                return raced;
            }

            var stream = await TryConnectAsync(ct).ConfigureAwait(false);

            if (stream is null && startDaemon is not null && await startDaemon().ConfigureAwait(false))
            {
                var deadline = Stopwatch.StartNew();
                while (stream is null && deadline.Elapsed < TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                    stream = await TryConnectAsync(ct).ConfigureAwait(false);
                }
            }

            if (stream is null)
            {
                throw new MuxUnavailableException($"fleetd is not running at {endpoint.Address}");
            }

            var wire = new Wire(stream);
            await wire.SendAsync(
                MessageType.Hello,
                new Hello { Version = Wire.Version, Role = ClientRoles.Control, Os = Environment.OSVersion.Platform.ToString() },
                WireJsonContext.Default.Hello,
                ct).ConfigureAwait(false);

            if (await wire.ReceiveAsync(ct).ConfigureAwait(false) is not { } welcome)
            {
                throw new MuxUnavailableException("fleetd closed the connection");
            }

            if (welcome.Type == MessageType.Error)
            {
                throw new MuxUnavailableException(Wire.Read(welcome.Payload, WireJsonContext.Default.ErrorMessage).Message);
            }

            _ = Task.Run(() => ReadLoopAsync(wire), CancellationToken.None);
            _wire = wire;
            return wire;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task<Stream?> TryConnectAsync(CancellationToken ct)
    {
        if (open is not null)
        {
            return await open(ct).ConfigureAwait(false);
        }

        try
        {
            return await endpoint.ConnectAsync(ConnectTimeout, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or TimeoutException or System.Net.Sockets.SocketException
                                      or OperationCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task ReadLoopAsync(Wire wire)
    {
        try
        {
            while (await wire.ReceiveAsync().ConfigureAwait(false) is { } message)
            {
                if (message.Type != MessageType.Response)
                {
                    continue;
                }

                var response = Wire.Read(message.Payload, WireJsonContext.Default.ControlResponse);
                if (_pending.TryRemove(response.Id, out var waiter))
                {
                    waiter.TrySetResult(response);
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or EndOfStreamException
                                      or InvalidDataException or System.Text.Json.JsonException)
        {
        }

        if (ReferenceEquals(_wire, wire))
        {
            _wire = null;
        }

        foreach (var waiter in _pending.Values)
        {
            waiter.TrySetException(new IOException("fleetd closed the connection"));
        }
    }
}
