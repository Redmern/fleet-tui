using Fleet.Platform.Forwards;
using Fleet.Platform.Forwards.Models;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public sealed class ForwardOptions
{
    public Func<string, string?> ControlPath { get; init; } = _ => null;

    public Func<IReadOnlyList<string>, CancellationToken, Task<SshResult>> Ssh { get; init; } = SshProcess.RunAsync;

    public LocalPorts Locals { get; init; } = LocalPorts.Loopback();

    public Func<string, string?> OpenBrowser { get; init; } = url => $"cannot open {url} here";

    public Func<string, CancellationToken, Task<bool>> Healthy { get; init; } = HealthCheck.OkAsync;

    public TimeSpan ScanEvery { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan ForwardWithin { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan StackWithin { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan ReconnectAfter { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ReconnectAtMost { get; init; } = TimeSpan.FromMinutes(1);
}

public sealed class ForwardHub(ForwardOptions options, Action<string> log)
{
    public const string AddOp = "forward-add";
    public const string RemoveOp = "forward-remove";
    public const string ListOp = "list-forwards";
    public const string StackStartOp = "stack-start";
    public const string StackStopOp = "stack-stop";
    public const string ViewerOpenOp = "viewer-open";

    public static readonly IReadOnlySet<string> SlowOps = new HashSet<string>(StringComparer.Ordinal)
    {
        AddOp, RemoveOp, StackStartOp, StackStopOp,
    };

    private readonly Lock _gate = new();
    private readonly Dictionary<string, HostForwards> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _stackPins = new(StringComparer.OrdinalIgnoreCase);

    public HostForwards? For(string host)
    {
        lock (_gate)
        {
            if (_hosts.TryGetValue(host, out var known))
            {
                return known;
            }

            if (options.ControlPath(host) is not { } path)
            {
                return null;
            }

            var made = new HostForwards(host, path, options.Locals, options.Ssh, log);
            _hosts[host] = made;
            return made;
        }
    }

    public void Watch(RemoteLink link, Func<RemoteLink, bool> current, CancellationToken stop)
    {
        if (For(link.Host) is not { } forwards)
        {
            return;
        }

        link.Errored += line =>
        {
            if (SshControl.Prohibited(line))
            {
                forwards.Prohibited();
            }
        };

        _ = Task.Run(() => LoopAsync(link, forwards, current, stop), CancellationToken.None);
    }

    public TimeSpan? Ended(RemoteLink link, bool stillWanted)
    {
        HostForwards? forwards;
        lock (_gate)
        {
            _hosts.TryGetValue(link.Host, out forwards);
        }

        if (forwards is null)
        {
            return null;
        }

        var wanted = forwards.Wanted;
        if (stillWanted)
        {
            forwards.Unlinked();
        }

        bool retrying;
        lock (_gate)
        {
            retrying = _failures.ContainsKey(link.Host);
        }

        if (!stillWanted || !wanted || !(link.WasConnected || retrying))
        {
            lock (_gate)
            {
                _failures.Remove(link.Host);
            }

            return null;
        }

        lock (_gate)
        {
            var failures = _failures[link.Host] = _failures.GetValueOrDefault(link.Host) + 1;
            var wait = options.ReconnectAfter * Math.Pow(2, Math.Min(failures - 1, 6));
            return wait < options.ReconnectAtMost ? wait : options.ReconnectAtMost;
        }
    }

    public void Dropped(string host)
    {
        HostForwards? forwards;
        lock (_gate)
        {
            _hosts.Remove(host, out forwards);
            _failures.Remove(host);
            foreach (var stack in _stacks.Keys.Where(k => k.StartsWith(host + "\n", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _stacks.Remove(stack);
                _stackPins.Remove(stack);
            }
        }

        forwards?.Unlinked();
    }

    public List<ForwardDto> List()
    {
        List<HostForwards> hosts;
        lock (_gate)
        {
            hosts = [.. _hosts.Values];
        }

        return [.. hosts.SelectMany(h => h.Rows()).Select(ToDto)];
    }

    public void Open(RemoteLink link, string? value)
    {
        if (!int.TryParse(value, out var port) || For(link.Host)?.Row(port).Url is not { } url)
        {
            log($"remote {link.Host}: asked to open port {value}, which is not forwarded here");
            return;
        }

        if (options.OpenBrowser(url) is { } failed)
        {
            log($"remote {link.Host}: {failed}");
        }
    }

    public async Task<ControlResponse> HandleAsync(
        ControlRequest request, Func<string, RemoteLink?> linkOf, Action<string> connect, CancellationToken ct)
    {
        var host = request.Host is { Length: > 0 } named ? named : throw new InvalidOperationException($"{request.Op} needs a host");
        var forwards = For(host)
            ?? throw new InvalidOperationException("port forwarding needs OpenSSH control sockets, which this system does not have");

        switch (request.Op)
        {
            case AddOp:
                {
                    var port = Port(request.Port);
                    int? local = request.LocalPort > 0 ? Port(request.LocalPort) : null;
                    var pinnedBefore = forwards.IsPinned(port);
                    forwards.Pin(port, local);
                    try
                    {
                        await ConnectedAsync(host, linkOf, connect, options.ForwardWithin, ct).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException) when (!pinnedBefore)
                    {
                        forwards.Unpin(port);
                        throw;
                    }

                    await forwards.ForwardNowAsync(port, local, ct).ConfigureAwait(false);
                    var row = await SettledAsync(forwards, port, options.ForwardWithin, ct).ConfigureAwait(false);
                    return new ControlResponse { Ok = true, Forwards = [ToDto(row)] };
                }

            case RemoveOp:
                await forwards.UnforwardNowAsync(Port(request.Port), ct).ConfigureAwait(false);
                return new ControlResponse { Ok = true };

            case StackStartOp:
                return new ControlResponse
                {
                    Ok = true,
                    Forwards = [ToDto(await StartStackAsync(host, Project(request), forwards, linkOf, connect, ct).ConfigureAwait(false))],
                };

            case StackStopOp:
                await StopStackAsync(host, Project(request), linkOf).ConfigureAwait(false);
                return new ControlResponse { Ok = true };

            default:
                throw new InvalidOperationException($"unknown operation {request.Op}");
        }
    }

    public static ForwardDto ToDto(PortForward row) =>
        new()
        {
            Host = row.Host,
            RemotePort = row.RemotePort,
            LocalPort = row.LocalPort,
            State = row.State.ToString().ToLowerInvariant(),
            Project = row.Project,
            Error = row.Error,
            Viewer = row.Viewer,
        };

    public static PortForward FromDto(ForwardDto dto) =>
        new(
            dto.Host,
            dto.RemotePort,
            dto.LocalPort,
            Enum.TryParse<ForwardState>(dto.State, ignoreCase: true, out var state) ? state : ForwardState.Detected,
            dto.Project,
            dto.Error,
            dto.Viewer);

    public static IReadOnlyDictionary<int, string> Allowed(RemoteLink link)
    {
        var running = link.RunningProjects;
        return HostForwards.Allowlist(link.Configs
            .Where(c => running.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .Select(c => (c.Name, (IReadOnlyList<int>)(c.ForwardPorts ?? []))));
    }

    private async Task LoopAsync(RemoteLink link, HostForwards forwards, Func<RemoteLink, bool> current, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested && current(link))
        {
            if (link.IsConnected)
            {
                try
                {
                    if (await forwards.ScanAsync(stop).ConfigureAwait(false) is { } listening)
                    {
                        await forwards.ReconcileAsync(listening, Allowed(link), stop).ConfigureAwait(false);
                        lock (_gate)
                        {
                            _failures.Remove(link.Host);
                        }
                    }

                    await link.ReportForwardsAsync([.. forwards.Rows().Select(ToDto)]).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e) when (e is IOException or InvalidOperationException)
                {
                    log($"remote {link.Host}: port scan failed: {e.Message}");
                }
            }
            else if (link.Snapshot().State == RemoteLink.Failed)
            {
                return;
            }

            try
            {
                await Task.Delay(options.ScanEvery, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<PortForward> StartStackAsync(
        string host,
        string project,
        HostForwards forwards,
        Func<string, RemoteLink?> linkOf,
        Action<string> connect,
        CancellationToken ct)
    {
        var link = await ConnectedAsync(host, linkOf, connect, options.ForwardWithin, ct).ConfigureAwait(false);
        var config = link.Configs.FirstOrDefault(c => string.Equals(c.Name, project, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"{link.Name} has no project {project}, or its fleet is too old to share project settings; update fleet there");

        if (config.RunCommand is not { Length: > 0 } command)
        {
            throw new InvalidOperationException($"{project} on {link.Name} has no runCommand in its project config");
        }

        var port = config.ReadyPort ?? config.ForwardPorts?.FirstOrDefault()
            ?? throw new InvalidOperationException($"{project} on {link.Name} needs readyPort or forwardPorts to know when it is up");

        await link.EnsureOpenAsync(config.Name).ConfigureAwait(false);

        var key = StackKey(host, config.Name);
        if (await StackGoneAsync(link, key).ConfigureAwait(false))
        {
            lock (_gate)
            {
                _stacks.Remove(key);
            }
        }

        bool started;
        lock (_gate)
        {
            started = !_stacks.TryAdd(key, string.Empty);
        }

        if (!started)
        {
            ControlResponse spawned;
            try
            {
                spawned = await link.SendAsync(new ControlRequest
                {
                    Op = "spawn",
                    Workspace = config.Name,
                    Cwd = config.Root,
                    Args = StackCommand(command),
                }).ConfigureAwait(false);
            }
            catch
            {
                lock (_gate)
                {
                    _stacks.Remove(key);
                }

                throw;
            }

            lock (_gate)
            {
                _stacks[key] = spawned.Pane ?? string.Empty;
            }

            log($"remote {host}: started the stack of {config.Name}: {command}");
        }

        if (!forwards.IsPinned(port))
        {
            forwards.Pin(port, null);
            lock (_gate)
            {
                _stackPins[key] = port;
            }
        }

        var stackClock = System.Diagnostics.Stopwatch.StartNew();
        var row = await SettledAsync(forwards, port, options.StackWithin, ct, waitForListening: true).ConfigureAwait(false);
        if (row.State != ForwardState.Forwarded)
        {
            throw new TimeoutException(
                row.Error is { } why
                    ? $"{config.Name} did not come up on port {port}: {why}"
                    : $"{config.Name} did not come up on port {port} within {options.StackWithin.TotalSeconds:0}s");
        }

        if (config.HealthPath is { Length: > 0 } path)
        {
            var url = $"http://127.0.0.1:{row.LocalPort}/{path.TrimStart('/')}";
            while (!await options.Healthy(url, ct).ConfigureAwait(false))
            {
                if (stackClock.Elapsed > options.StackWithin)
                {
                    throw new TimeoutException($"{config.Name} is listening on {port} but {path} did not answer 2xx");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }

        return row;
    }

    private async Task<bool> StackGoneAsync(RemoteLink link, string key)
    {
        string? pane;
        lock (_gate)
        {
            pane = _stacks.GetValueOrDefault(key);
        }

        if (pane is not { Length: > 0 })
        {
            return false;
        }

        var panes = (await link.SendAsync(new ControlRequest { Op = "list-panes" }).ConfigureAwait(false)).Panes ?? [];
        return panes.All(p => p.Id != pane);
    }

    private static string StackKey(string host, string project) => $"{host}\n{project}";

    public static List<string> StackCommand(string command) => ["sh", "-lc", command];

    private async Task StopStackAsync(string host, string project, Func<string, RemoteLink?> linkOf)
    {
        var key = StackKey(host, project);
        string? pane;
        lock (_gate)
        {
            pane = _stacks.GetValueOrDefault(key);
        }

        if (pane is not { Length: > 0 })
        {
            throw new InvalidOperationException($"fleet has not started the stack of {project} on {host}");
        }

        if (linkOf(host) is not { IsConnected: true } link)
        {
            throw new InvalidOperationException($"{host} is not connected");
        }

        await link.SendAsync(new ControlRequest { Op = "kill", Pane = pane }).ConfigureAwait(false);
        int? pinned = null;
        lock (_gate)
        {
            if (_stacks.GetValueOrDefault(key) == pane)
            {
                _stacks.Remove(key);
            }

            if (_stackPins.Remove(key, out var port))
            {
                pinned = port;
            }
        }

        if (pinned is { } unpin && For(host) is { } forwards)
        {
            forwards.Unpin(unpin);
        }

        log($"remote {host}: stopped the stack of {project}");
    }

    private static async Task<RemoteLink> ConnectedAsync(
        string host, Func<string, RemoteLink?> linkOf, Action<string> connect, TimeSpan within, CancellationToken ct)
    {
        if (linkOf(host) is not { } link || link.Snapshot().State == RemoteLink.Failed)
        {
            connect(host);
        }

        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (linkOf(host) is { } now)
            {
                var state = now.Snapshot();
                if (now.IsConnected)
                {
                    return now;
                }

                if (state.State == RemoteLink.Failed)
                {
                    throw new InvalidOperationException($"could not connect to {host}: {state.Error}");
                }

                if (state.State == RemoteLink.Asking)
                {
                    throw new InvalidOperationException($"ssh to {host} is asking \"{state.Prompt?.Trim()}\"; answer it under Remote machines");
                }
            }

            if (waited.Elapsed > within)
            {
                throw new TimeoutException($"{host} is still connecting");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
        }
    }

    private static async Task<PortForward> SettledAsync(
        HostForwards forwards, int port, TimeSpan within, CancellationToken ct, bool waitForListening = false)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var row = forwards.Row(port);
            var settled = row.State is ForwardState.Forwarded or ForwardState.Failed
                || (!waitForListening && row.State == ForwardState.Waiting && waited.Elapsed > TimeSpan.FromSeconds(5));

            if (settled || waited.Elapsed > within)
            {
                return row;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }
    }

    private static int Port(int port) =>
        port is > 0 and <= 65535 ? port : throw new InvalidOperationException($"{port} is not a port");

    private static string Project(ControlRequest request) =>
        request.Workspace is { Length: > 0 } project ? project : throw new InvalidOperationException($"{request.Op} needs a project");
}
