using System.Diagnostics;
using Fleet.Platform.Forwards.Models;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;

namespace Fleet.Platform.Forwards;

public sealed class HostForwards(
    string host,
    string? controlPath,
    LocalPorts locals,
    Func<IReadOnlyList<string>, CancellationToken, Task<SshResult>> ssh,
    Action<string> log,
    TunnelMode? tunnels = null)
{
    public const string ProhibitedError = "the ssh server refuses port forwarding (AllowTcpForwarding is off)";

    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly Lock _gate = new();
    private readonly Dictionary<int, int?> _pins = [];
    private readonly HashSet<int> _suppressed = [];
    private readonly Dictionary<int, Active> _active = [];
    private readonly Dictionary<int, string> _failed = [];
    private readonly Dictionary<int, int> _preferred = [];
    private IReadOnlyDictionary<int, IReadOnlyList<string>> _listening = new Dictionary<int, IReadOnlyList<string>>();
    private IReadOnlyDictionary<int, string> _allowed = new Dictionary<int, string>();
    private string? _hostError;
    private bool _linked;
    private readonly HashSet<int> _restore = [];
    private readonly Dictionary<int, int> _deaths = [];
    private readonly Dictionary<int, (DateTimeOffset At, string Why)> _retry = [];
    private bool _closed;
    private int _epoch;

    private sealed record Active(int Local, string Target, string? Project, ITunnel? Tunnel = null, DateTimeOffset Since = default);

    private static readonly IReadOnlyList<string> TunnelTarget = ["localhost"];

    public string Host => host;

    public string? ControlPath => controlPath;

    public bool Wanted
    {
        get
        {
            lock (_gate)
            {
                return _pins.Count > 0 || _active.Count > 0 || _restore.Count > 0;
            }
        }
    }

    public void Pin(int remotePort, int? localPort)
    {
        lock (_gate)
        {
            if (!_pins.ContainsKey(remotePort))
            {
                _deaths.Remove(remotePort);
            }

            _pins[remotePort] = localPort;
            _suppressed.Remove(remotePort);
            _failed.Remove(remotePort);
            _retry.Remove(remotePort);
        }
    }

    public bool IsPinned(int remotePort)
    {
        lock (_gate)
        {
            return _pins.ContainsKey(remotePort);
        }
    }

    public void Unpin(int remotePort)
    {
        lock (_gate)
        {
            _pins.Remove(remotePort);
            _restore.Remove(remotePort);
            _failed.Remove(remotePort);
            _retry.Remove(remotePort);
            _deaths.Remove(remotePort);
            if (_allowed.ContainsKey(remotePort))
            {
                _suppressed.Add(remotePort);
            }
        }
    }

    public void Prohibited()
    {
        lock (_gate)
        {
            _hostError = ProhibitedError;
        }
    }

    public void Unlinked()
    {
        lock (_gate)
        {
            _linked = false;
            _epoch++;
            _hostError = null;
            _restore.UnionWith(_active.Keys);
            foreach (var (remote, active) in _active)
            {
                active.Tunnel?.Kill();
                _preferred[remote] = active.Local;
                locals.Release(active.Local);
            }

            _active.Clear();
            _retry.Clear();
            _listening = new Dictionary<int, IReadOnlyList<string>>();
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _epoch++;
            foreach (var active in _active.Values)
            {
                active.Tunnel?.Kill();
                locals.Release(active.Local);
            }

            _active.Clear();
        }
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>?> ScanAsync(CancellationToken ct)
    {
        if (controlPath is null)
        {
            lock (_gate)
            {
                return PinnedLocked();
            }
        }

        var scan = await ssh(SshControl.Run(controlPath, host, ListeningPorts.Command), ct).ConfigureAwait(false);
        return scan.Ok || scan.Output.Length > 0 ? ListeningPorts.ByPort(ListeningPorts.Parse(scan.Output)) : null;
    }

    public async Task ReconcileAsync(
        IReadOnlyDictionary<int, IReadOnlyList<string>> listening,
        IReadOnlyDictionary<int, string> allowed,
        CancellationToken ct)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<(int Remote, Active Active)> cancel;
            List<(int Remote, string Target, string? Project, int? Local)> add;
            int epoch;

            lock (_gate)
            {
                if (_closed)
                {
                    return;
                }

                if (controlPath is null)
                {
                    ReapLocked();
                    listening = PinnedLocked();
                }

                _linked = true;
                epoch = _epoch;
                _restore.Clear();
                _listening = listening;
                _allowed = allowed;

                foreach (var gone in _suppressed.Where(p => !listening.ContainsKey(p)).ToList())
                {
                    _suppressed.Remove(gone);
                }

                foreach (var gone in _failed.Keys.Where(p => !listening.ContainsKey(p)).ToList())
                {
                    _failed.Remove(gone);
                }

                var wanted = Wants(listening, allowed);
                cancel = [.. _active
                    .Where(a => !wanted.TryGetValue(a.Key, out var target) || target != a.Value.Target || Moved(a.Key, a.Value))
                    .Select(a => (a.Key, a.Value))];
                add = [.. wanted
                    .Where(w => !_failed.ContainsKey(w.Key) && !BackingOffLocked(w.Key)
                        && (!_active.TryGetValue(w.Key, out var active) || active.Target != w.Value || Moved(w.Key, active)))
                    .Select(w => (w.Key, w.Value, allowed.GetValueOrDefault(w.Key), _pins.GetValueOrDefault(w.Key)))];
            }

            foreach (var (remote, active) in cancel)
            {
                await CancelAsync(remote, active, ct).ConfigureAwait(false);
            }

            foreach (var (remote, target, project, local) in add)
            {
                await ForwardAsync(remote, target, project, local, epoch, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    public async Task<PortForward> ForwardNowAsync(int remotePort, int? localPort, CancellationToken ct)
    {
        Pin(remotePort, localPort);
        IReadOnlyDictionary<int, IReadOnlyList<string>> listening;
        IReadOnlyDictionary<int, string> allowed;
        bool linked;

        lock (_gate)
        {
            listening = _listening;
            allowed = _allowed;
            linked = _linked;
        }

        if (linked)
        {
            await ReconcileAsync(listening, allowed, ct).ConfigureAwait(false);
        }

        return Row(remotePort);
    }

    public async Task UnforwardNowAsync(int remotePort, CancellationToken ct)
    {
        Unpin(remotePort);
        if (controlPath is null)
        {
            lock (_gate)
            {
                _listening = PinnedLocked();
            }
        }

        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Active? active;
            lock (_gate)
            {
                active = _active.GetValueOrDefault(remotePort);
            }

            if (active is not null)
            {
                await CancelAsync(remotePort, active, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    public PortForward Row(int remotePort)
    {
        lock (_gate)
        {
            return RowLocked(remotePort);
        }
    }

    public IReadOnlyList<PortForward> Rows()
    {
        lock (_gate)
        {
            return [.. _listening.Keys
                .Concat(_pins.Keys)
                .Concat(_active.Keys)
                .Distinct()
                .Order()
                .Select(RowLocked)];
        }
    }

    public static Dictionary<int, string> Allowlist(IEnumerable<(string Project, IReadOnlyList<int> Ports)> projects)
    {
        var allowed = new Dictionary<int, string>();
        foreach (var (project, ports) in projects)
        {
            foreach (var port in ports)
            {
                allowed.TryAdd(port, project);
            }
        }

        return allowed;
    }

    private Dictionary<int, string> Wants(
        IReadOnlyDictionary<int, IReadOnlyList<string>> listening,
        IReadOnlyDictionary<int, string> allowed) =>
        allowed.Keys
            .Where(p => !_suppressed.Contains(p))
            .Concat(_pins.Keys)
            .Distinct()
            .Where(listening.ContainsKey)
            .ToDictionary(p => p, p => ListeningPorts.Target(listening[p]));

    private TunnelMode Mode =>
        tunnels ?? throw new InvalidOperationException($"remote {host} has neither an ssh control socket nor a tunnel launcher");

    private DateTimeOffset Now => Mode.Time.GetUtcNow();

    private bool BackingOffLocked(int remote) => _retry.TryGetValue(remote, out var retry) && retry.At > Now;

    private void ReapLocked()
    {
        foreach (var (remote, active) in _active.Where(a => a.Value.Tunnel is { Exited.IsCompleted: true }).ToList())
        {
            _active.Remove(remote);
            _preferred[remote] = active.Local;
            locals.Release(active.Local);
            var why = Why(active.Tunnel!);
            BackOffLocked(remote, why, lastedLong: Now - active.Since >= Mode.RestartAtMost);
        }
    }

    private void BackOffLocked(int remote, string why, bool lastedLong)
    {
        var deaths = _deaths[remote] = (lastedLong ? 0 : _deaths.GetValueOrDefault(remote)) + 1;
        var wait = Mode.RestartAfter * Math.Pow(2, Math.Min(deaths - 1, 16));
        wait = wait < Mode.RestartAtMost ? wait : Mode.RestartAtMost;
        _retry[remote] = (Now + wait, why);
        log($"remote {host}: the ssh tunnel of {remote} exited ({why}); restarting in {wait.TotalSeconds:0}s");
    }

    private Dictionary<int, IReadOnlyList<string>> PinnedLocked() => _pins.Keys.ToDictionary(p => p, _ => TunnelTarget);

    private bool Moved(int remote, Active active) =>
        _pins.GetValueOrDefault(remote) is { } wanted && wanted != active.Local;

    private PortForward RowLocked(int remote)
    {
        var project = _allowed.GetValueOrDefault(remote);

        if (_active.TryGetValue(remote, out var dead) && dead.Tunnel is { Exited.IsCompleted: true } exited)
        {
            return new PortForward(
                host, remote, null, ForwardState.Waiting, project, $"the ssh tunnel exited ({Why(exited)})");
        }

        if (_retry.TryGetValue(remote, out var retry))
        {
            var left = retry.At - Now;
            return new PortForward(
                host,
                remote,
                null,
                ForwardState.Waiting,
                project,
                $"the ssh tunnel exited ({retry.Why}); restarting in {Math.Max(0, Math.Ceiling(left.TotalSeconds)):0}s");
        }

        if (_active.TryGetValue(remote, out var active))
        {
            return new PortForward(host, remote, active.Local, ForwardState.Forwarded, active.Project ?? project, _hostError);
        }

        if (_failed.TryGetValue(remote, out var error))
        {
            return new PortForward(host, remote, null, ForwardState.Failed, project, error);
        }

        if (_pins.ContainsKey(remote) || (_allowed.ContainsKey(remote) && !_suppressed.Contains(remote)))
        {
            return new PortForward(
                host,
                remote,
                null,
                ForwardState.Waiting,
                project,
                _linked ? $"nothing listens on port {remote} yet" : "the link is down");
        }

        return new PortForward(host, remote, null, ForwardState.Detected, project);
    }

    private async Task ForwardAsync(int remote, string target, string? project, int? wantedLocal, int epoch, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_closed || _epoch != epoch)
            {
                return;
            }
        }

        int local;
        try
        {
            int? preferred;
            lock (_gate)
            {
                preferred = _preferred.TryGetValue(remote, out var before) ? before : null;
            }

            local = locals.Take(wantedLocal ?? preferred ?? remote);
            if (wantedLocal is { } exact && local != exact)
            {
                locals.Release(local);
                Fail(remote, $"local port {exact} is in use");
                return;
            }
        }
        catch (IOException e)
        {
            Fail(remote, e.Message);
            return;
        }

        ITunnel? tunnel;
        string? failed;
        try
        {
            (tunnel, failed) = controlPath is null
                ? await OpenTunnelAsync(local, target, remote, ct).ConfigureAwait(false)
                : (null, await ForwardOverMasterAsync(controlPath, local, target, remote, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            locals.Release(local);
            throw;
        }

        if (failed is { } why)
        {
            locals.Release(local);
            lock (_gate)
            {
                if (controlPath is null && _deaths.ContainsKey(remote) && !SshControl.Prohibited(why))
                {
                    BackOffLocked(remote, why, lastedLong: false);
                    return;
                }
            }

            Fail(remote, SshControl.Prohibited(why) ? ProhibitedError : why);
            return;
        }

        lock (_gate)
        {
            if (_closed || _epoch != epoch)
            {
                tunnel?.Kill();
                locals.Release(local);
                return;
            }

            _retry.Remove(remote);
            _active[remote] = new Active(local, target, project, tunnel, controlPath is null ? Now : default);
            _preferred[remote] = local;
        }

        log($"remote {host}: forwarded localhost:{local} to {target}:{remote}");
    }

    private async Task<string?> ForwardOverMasterAsync(string master, int local, string target, int remote, CancellationToken ct)
    {
        var result = await ssh(SshControl.Forward(master, host, local, target, remote), ct).ConfigureAwait(false);
        return result.Ok ? null : Why(result.Exit, result.Errors);
    }

    private async Task<(ITunnel? Tunnel, string? Error)> OpenTunnelAsync(int local, string target, int remote, CancellationToken ct)
    {
        var mode = Mode;
        var tunnel = mode.Launcher.Start(SshControl.Tunnel(host, local, target, remote));
        var waited = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                if (tunnel.Exited.IsCompleted)
                {
                    return (null, Why(tunnel));
                }

                if (await mode.Probe.ListeningAsync(local, ct).ConfigureAwait(false))
                {
                    return (tunnel, null);
                }

                if (waited.Elapsed > mode.UpWithin)
                {
                    tunnel.Kill();
                    return (null, $"ssh did not open localhost:{local} within {mode.UpWithin.TotalSeconds:0}s");
                }

                await Task.WhenAny(tunnel.Exited, Task.Delay(TimeSpan.FromMilliseconds(200), ct)).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            tunnel.Kill();
            throw;
        }
    }

    private static string Why(int exit, string errors) =>
        errors.Trim() is { Length: > 0 } said ? said : $"ssh exited with {exit}";

    private static string Why(ITunnel tunnel) =>
        Why(tunnel.Exited.IsCompletedSuccessfully ? tunnel.Exited.Result : -1, tunnel.Errors);

    private async Task CancelAsync(int remote, Active active, CancellationToken ct)
    {
        if (active.Tunnel is { } tunnel)
        {
            tunnel.Kill();
            await Task.WhenAny(tunnel.Exited, Task.Delay(TimeSpan.FromSeconds(2), ct)).ConfigureAwait(false);
        }
        else if (controlPath is not null)
        {
            var result = await ssh(SshControl.Cancel(controlPath, host, active.Local, active.Target, remote), ct).ConfigureAwait(false);
            if (!result.Ok)
            {
                log($"remote {host}: could not cancel the forward of {remote}: {result.Errors.Trim()}");
            }
        }

        lock (_gate)
        {
            if (!_active.TryGetValue(remote, out var current) || current != active)
            {
                return;
            }

            _active.Remove(remote);
        }

        locals.Release(active.Local);
        log($"remote {host}: stopped forwarding localhost:{active.Local}");
    }

    private void Fail(int remote, string error)
    {
        lock (_gate)
        {
            _failed[remote] = error;
        }

        log($"remote {host}: could not forward {remote}: {error}");
    }
}
