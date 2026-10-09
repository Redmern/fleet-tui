using Fleet.Platform.Forwards.Models;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;

namespace Fleet.Platform.Forwards;

public sealed class HostForwards(
    string host,
    string controlPath,
    LocalPorts locals,
    Func<IReadOnlyList<string>, CancellationToken, Task<SshResult>> ssh,
    Action<string> log)
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

    private sealed record Active(int Local, string Target, string? Project);

    public string Host => host;

    public string ControlPath => controlPath;

    public bool Wanted
    {
        get
        {
            lock (_gate)
            {
                return _pins.Count > 0 || _active.Count > 0;
            }
        }
    }

    public void Pin(int remotePort, int? localPort)
    {
        lock (_gate)
        {
            _pins[remotePort] = localPort;
            _suppressed.Remove(remotePort);
            _failed.Remove(remotePort);
        }
    }

    public void Unpin(int remotePort)
    {
        lock (_gate)
        {
            _pins.Remove(remotePort);
            _failed.Remove(remotePort);
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
            foreach (var (remote, active) in _active)
            {
                _preferred[remote] = active.Local;
                locals.Release(active.Local);
            }

            _active.Clear();
            _listening = new Dictionary<int, IReadOnlyList<string>>();
        }
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>?> ScanAsync(CancellationToken ct)
    {
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

            lock (_gate)
            {
                _linked = true;
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
                    .Where(a => !wanted.TryGetValue(a.Key, out var target) || target != a.Value.Target)
                    .Select(a => (a.Key, a.Value))];
                add = [.. wanted
                    .Where(w => !_failed.ContainsKey(w.Key)
                        && (!_active.TryGetValue(w.Key, out var active) || active.Target != w.Value))
                    .Select(w => (w.Key, w.Value, allowed.GetValueOrDefault(w.Key), _pins.GetValueOrDefault(w.Key)))];
            }

            foreach (var (remote, active) in cancel)
            {
                await CancelAsync(remote, active, ct).ConfigureAwait(false);
            }

            foreach (var (remote, target, project, local) in add)
            {
                await ForwardAsync(remote, target, project, local, ct).ConfigureAwait(false);
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

    public async Task CloseAsync(CancellationToken ct)
    {
        List<(int Remote, Active Active)> open;
        lock (_gate)
        {
            open = [.. _active.Select(a => (a.Key, a.Value))];
        }

        foreach (var (remote, active) in open)
        {
            await CancelAsync(remote, active, ct).ConfigureAwait(false);
        }

        lock (_gate)
        {
            _pins.Clear();
            _suppressed.Clear();
            _failed.Clear();
            _preferred.Clear();
        }
    }

    public bool IsListening(int remotePort)
    {
        lock (_gate)
        {
            return _listening.ContainsKey(remotePort);
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

    private PortForward RowLocked(int remote)
    {
        var project = _allowed.GetValueOrDefault(remote);

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

    private async Task ForwardAsync(int remote, string target, string? project, int? wantedLocal, CancellationToken ct)
    {
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

        var result = await ssh(SshControl.Forward(controlPath, host, local, target, remote), ct).ConfigureAwait(false);
        if (!result.Ok)
        {
            locals.Release(local);
            var why = result.Errors.Trim() is { Length: > 0 } errors ? errors : $"ssh exited with {result.Exit}";
            Fail(remote, SshControl.Prohibited(why) ? ProhibitedError : why);
            return;
        }

        lock (_gate)
        {
            _active[remote] = new Active(local, target, project);
            _preferred[remote] = local;
        }

        log($"remote {host}: forwarded localhost:{local} to {target}:{remote}");
    }

    private async Task CancelAsync(int remote, Active active, CancellationToken ct)
    {
        var result = await ssh(SshControl.Cancel(controlPath, host, active.Local, active.Target, remote), ct).ConfigureAwait(false);
        if (!result.Ok)
        {
            log($"remote {host}: could not cancel the forward of {remote}: {result.Errors.Trim()}");
        }

        lock (_gate)
        {
            if (_active.TryGetValue(remote, out var current) && current == active)
            {
                _active.Remove(remote);
            }
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
