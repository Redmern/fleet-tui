using System.Collections.Concurrent;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Platform.Mux.Embedded.Pty;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public sealed record RemoteChannel(Stream Stream, TextReader? Errors, IDisposable Owner);

public sealed class RemoteLink(string host, Func<string, RemoteChannel> open, Action<string> log)
{
    public const string Connecting = "connecting";
    public const string Asking = "asking";
    public const string Connected = "connected";
    public const string Failed = "failed";

    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<string> _errors = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ControlResponse>> _pending = new();
    private Wire? _wire;
    private int _nextId;
    private string _state = Connecting;
    private string? _name;
    private string? _error;
    private string? _prompt;
    private string? _answer;
    private IReadOnlyList<string> _projects = [];
    private IReadOnlyList<string> _runningProjects = [];

    public static readonly TimeSpan OpenWithin = TimeSpan.FromMinutes(1);

    public string Host => host;

    public string Name
    {
        get
        {
            lock (_gate)
            {
                return _name ?? host;
            }
        }
    }

    public string Token { get; } = Guid.NewGuid().ToString("N");

    public RemotePty Pty { get; private set; } = new();

    public event Action<HostEffect>? Effect;

    public event Action<IReadOnlyList<NoticeDto>>? Noticed;

    private IReadOnlyList<NoticeDto> _notices = [];
    private HashSet<string>? _openBefore;

    public IReadOnlyList<NoticeDto> Notices
    {
        get
        {
            lock (_gate)
            {
                return _notices;
            }
        }
    }

    public static IReadOnlyList<NoticeDto> Fresh(ISet<string>? openBefore, IReadOnlyList<NoticeDto> now) =>
        openBefore is null ? [] : [.. now.Where(n => n.IsOpen && !openBefore.Contains(NoticeId(n)))];

    public static string NoticeId(NoticeDto notice) => $"{notice.Project}|{notice.Key}";

    public void Dismiss(string project, IReadOnlyList<string> keys) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await RequestAsync(new ControlRequest { Op = "dismiss-notices", Workspace = project, Args = [.. keys] }, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
            {
                log($"remote {host}: could not dismiss notices of {project}: {e.Message}");
            }
        });

    private async Task PollNoticesAsync(CancellationToken ct)
    {
        IReadOnlyList<NoticeDto> now;
        try
        {
            now = (await RequestAsync(new ControlRequest { Op = "list-notices" }, ct).ConfigureAwait(false)).Notices ?? [];
        }
        catch (InvalidOperationException)
        {
            return;
        }

        IReadOnlyList<NoticeDto> fresh;
        lock (_gate)
        {
            fresh = Fresh(_openBefore, now);
            _openBefore = [.. now.Where(n => n.IsOpen).Select(NoticeId)];
            _notices = now;
        }

        Noticed?.Invoke(fresh);
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _state == Connected;
            }
        }
    }

    public RemoteDto Snapshot()
    {
        lock (_gate)
        {
            return new RemoteDto
            {
                Host = host,
                Name = _name ?? host,
                State = _state,
                Error = _error,
                Prompt = _state == Asking ? _prompt : null,
                Secret = _prompt is not null && IsSecret(_prompt),
                Projects = [.. _projects],
                Running = [.. _runningProjects],
            };
        }
    }

    public static bool IsSecret(string prompt) => !prompt.Contains("yes/no", StringComparison.OrdinalIgnoreCase);

    public static string Label(string name) => $" @{name}";

    public (bool Pending, string? Answer) Ask(string prompt)
    {
        lock (_gate)
        {
            if (_answer is { } answer && _prompt == prompt)
            {
                _answer = null;
                _prompt = null;
                _state = Connecting;
                return (false, answer);
            }

            _prompt = prompt;
            _state = Asking;
            return (true, null);
        }
    }

    public void Answer(string answer)
    {
        lock (_gate)
        {
            _answer = answer;
        }
    }

    public void Stop() => _stop.Cancel();

    public string? Showing { get; private set; }

    public async Task ShowAsync(string project)
    {
        if (!await RunningAsync(project).ConfigureAwait(false))
        {
            await RequestAsync(new ControlRequest { Op = "open-project", Workspace = project }, _stop.Token).ConfigureAwait(false);
            var waited = System.Diagnostics.Stopwatch.StartNew();

            while (!await RunningAsync(project).ConfigureAwait(false))
            {
                if (waited.Elapsed > OpenWithin)
                {
                    throw new TimeoutException($"{project} did not start on {Name}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300), _stop.Token).ConfigureAwait(false);
            }
        }

        await RequestAsync(new ControlRequest { Op = "show", Workspace = project }, _stop.Token).ConfigureAwait(false);
        Showing = project;
    }

    public async Task NewProjectAsync()
    {
        if (_wire is not { } wire)
        {
            throw new InvalidOperationException($"{Name} is not connected");
        }

        await wire.SendAsync(
            MessageType.Command,
            new CommandMessage { Name = "menu", Arg = FleetActionIds.For(FleetAction.NewProject) },
            WireJsonContext.Default.CommandMessage,
            _stop.Token).ConfigureAwait(false);
        Showing = null;
    }

    private async Task<bool> RunningAsync(string project) =>
        ((await RequestAsync(new ControlRequest { Op = "list-workspaces" }, _stop.Token).ConfigureAwait(false)).Workspaces ?? [])
            .Any(w => string.Equals(w.Name, project, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Listed(IEnumerable<string> saved, IEnumerable<string> running) =>
        [.. saved.Concat(running.Where(n => !FleetWorkspaces.IsHidden(n)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    public async Task ForwardAsync(MessageType type, byte[] payload)
    {
        if (_wire is not { } wire)
        {
            return;
        }

        try
        {
            await wire.SendAsync(type, payload, _stop.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    public async Task RunAsync(CancellationToken daemon)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(daemon, _stop.Token);
        var ct = linked.Token;
        RemoteChannel? channel = null;

        try
        {
            channel = open(Token);
            if (channel.Errors is { } errors)
            {
                _ = Task.Run(() => CollectErrorsAsync(errors), CancellationToken.None);
            }

            var wire = new Wire(channel.Stream);
            var (cols, rows) = Pty.Size;
            await wire.SendAsync(
                MessageType.Hello,
                new Hello
                {
                    Version = Wire.OldestVersion,
                    Highest = Wire.Version,
                    Build = FleetVersion.Current,
                    Role = ClientRoles.Attach,
                    Os = OperatingSystem.IsWindows() ? "windows" : "unix",
                    Cols = cols,
                    Rows = rows,
                    Label = Label(host),
                },
                WireJsonContext.Default.Hello,
                ct).ConfigureAwait(false);

            if (await wire.ReceiveAsync(ct).ConfigureAwait(false) is not { } welcome)
            {
                throw new IOException("the remote closed the connection");
            }

            if (welcome.Type == MessageType.Error)
            {
                throw new IOException(Wire.Read(welcome.Payload, WireJsonContext.Default.ErrorMessage).Message);
            }

            if (Wire.Refusal(Wire.Read(welcome.Payload, WireJsonContext.Default.Welcome)) is { } refused)
            {
                throw new IOException(refused);
            }

            _wire = wire;
            var reader = Task.Run(() => ReadLoopAsync(wire), CancellationToken.None);
            Pty.Resized += Resize;
            _running = true;

            var status = await RequestAsync(new ControlRequest { Op = "status" }, ct).ConfigureAwait(false);
            lock (_gate)
            {
                _name = status.Status?.Host is { Length: > 0 } name ? name : host;
            }

            await RequestAsync(new ControlRequest { Op = "set-label", Text = Label(Name) }, ct).ConfigureAwait(false);
            log($"remote {host}: connected to {Name}");

            while (!ct.IsCancellationRequested)
            {
                var workspaces = (await RequestAsync(new ControlRequest { Op = "list-workspaces" }, ct).ConfigureAwait(false)).Workspaces ?? [];
                IReadOnlyList<string> saved;
                try
                {
                    saved = (await RequestAsync(new ControlRequest { Op = "list-projects" }, ct).ConfigureAwait(false)).Projects ?? [];
                }
                catch (InvalidOperationException)
                {
                    saved = [];
                }

                lock (_gate)
                {
                    _runningProjects = [.. workspaces.Select(w => w.Name).Where(n => !FleetWorkspaces.IsHidden(n))];
                    _projects = Listed(saved, _runningProjects);
                    _state = Connected;
                    _error = null;
                }

                await PollNoticesAsync(ct).ConfigureAwait(false);

                if (await Task.WhenAny(reader, Task.Delay(RefreshEvery, ct)).ConfigureAwait(false) == reader)
                {
                    throw new IOException("the remote went away");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException
                                      or TimeoutException or System.Text.Json.JsonException
                                      or System.ComponentModel.Win32Exception)
        {
            await Task.Delay(300, CancellationToken.None).ConfigureAwait(false);

            lock (_gate)
            {
                _state = Failed;
                _error = _errors.Count > 0 ? string.Join(" ", _errors) : e.Message;
            }

            log($"remote {host}: {_error}");
        }
        finally
        {
            _running = false;
            Pty.Resized -= Resize;
            _wire = null;
            Pty.Exit();
            channel?.Owner.Dispose();

            foreach (var waiting in _pending.Values)
            {
                waiting.TrySetException(new IOException("the remote went away"));
            }
        }
    }

    private volatile bool _running;

    private void EndView()
    {
        var ended = Pty;
        var fresh = new RemotePty();
        ended.Resized -= Resize;

        if (_running)
        {
            fresh.Resized += Resize;
        }

        Pty = fresh;
        Showing = null;
        ended.Exit();
        log($"remote {host}: the remote closed its last project in this view");
    }

    private void Resize(int cols, int rows) =>
        _ = _wire?.SendAsync(MessageType.Resize, new ResizeMessage { Cols = cols, Rows = rows }, WireJsonContext.Default.ResizeMessage);

    public async Task<ControlResponse> HeadAsync(string tool, IReadOnlyDictionary<string, string> arguments)
    {
        try
        {
            return await RequestAsync(
                new ControlRequest { Op = HeadOp, Text = tool, Env = new Dictionary<string, string>(arguments) },
                _stop.Token,
                HeadWithin,
                raw: true).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new ControlResponse { Ok = false, Error = $"{Name} did not answer {tool} within {HeadWithin.TotalMinutes:0} minutes" };
        }
    }

    public const string HeadOp = "head";

    public static readonly TimeSpan HeadWithin = TimeSpan.FromMinutes(10);

    private async Task<ControlResponse> RequestAsync(
        ControlRequest request, CancellationToken ct, TimeSpan? within = null, bool raw = false)
    {
        var wire = _wire ?? throw new IOException("not connected");
        request.Id = Interlocked.Increment(ref _nextId);
        var reply = new TaskCompletionSource<ControlResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = reply;

        try
        {
            await wire.SendAsync(MessageType.Request, request, WireJsonContext.Default.ControlRequest, ct).ConfigureAwait(false);
            var response = await reply.Task.WaitAsync(within ?? AnswerWithin, ct).ConfigureAwait(false);
            return response.Ok || raw ? response : throw new InvalidOperationException($"remote: {response.Error}");
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    private async Task ReadLoopAsync(Wire wire)
    {
        try
        {
            while (await wire.ReceiveAsync().ConfigureAwait(false) is { } message)
            {
                switch (message.Type)
                {
                    case MessageType.Frame:
                        Pty.Emit(Wire.ReadFrame(message.Payload).Bytes);
                        break;
                    case MessageType.Response:
                        var response = Wire.Read(message.Payload, WireJsonContext.Default.ControlResponse);
                        if (_pending.TryRemove(response.Id, out var waiting))
                        {
                            waiting.TrySetResult(response);
                        }

                        break;
                    case MessageType.HostEffect:
                        Effect?.Invoke(Wire.Read(message.Payload, WireJsonContext.Default.HostEffect));
                        break;
                    case MessageType.Bye:
                        EndView();
                        break;
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or EndOfStreamException
                                      or InvalidDataException or System.Text.Json.JsonException)
        {
        }
    }

    private async Task CollectErrorsAsync(TextReader errors)
    {
        try
        {
            while (await errors.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                lock (_gate)
                {
                    _errors.Enqueue(line.Trim());
                    while (_errors.Count > 3)
                    {
                        _errors.Dequeue();
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }
}
