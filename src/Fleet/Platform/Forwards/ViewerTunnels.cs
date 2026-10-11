using Fleet.Platform.Forwards.Models;

namespace Fleet.Platform.Forwards;

public sealed class ViewerTunnels : IDisposable
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> NothingScanned = new Dictionary<int, IReadOnlyList<string>>();
    private static readonly IReadOnlyDictionary<int, string> NothingAllowed = new Dictionary<int, string>();

    private readonly HostForwards _forwards;
    private readonly TunnelMode _mode;
    private readonly Func<string, string?> _openBrowser;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();

    public ViewerTunnels(
        string host,
        TunnelMode mode,
        LocalPorts locals,
        Func<string, string?> openBrowser,
        Action<string> log,
        TimeSpan checkEvery)
    {
        _mode = mode;
        _openBrowser = openBrowser;
        _log = log;
        _forwards = new HostForwards(host, null, locals, NoMaster, log, mode);
        _ = Task.Run(() => RestartLoopAsync(checkEvery, _stop.Token), CancellationToken.None);
    }

    public async Task ForwardAsync(int port, CancellationToken ct)
    {
        _forwards.Pin(port, null);
        await _forwards.ReconcileAsync(NothingScanned, NothingAllowed, ct).ConfigureAwait(false);
        var row = _forwards.Row(port);
        if (row.Url is not { } url)
        {
            _log($"could not forward port {port}: {row.Error ?? row.State.ToString().ToLowerInvariant()}");
            return;
        }

        if (_openBrowser(url) is { } failed)
        {
            _log(failed);
        }
    }

    public Task UnforwardAsync(int port, CancellationToken ct) => _forwards.UnforwardNowAsync(port, ct);

    public void Apply(bool forward, int port) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await (forward ? ForwardAsync(port, _stop.Token) : UnforwardAsync(port, _stop.Token)).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException)
            {
                _log($"could not {(forward ? "forward" : "unforward")} port {port}: {e.Message}");
            }
        });

    public void Dispose()
    {
        _stop.Cancel();
        _forwards.Close();
        (_mode.Launcher as IDisposable)?.Dispose();
    }

    private async Task RestartLoopAsync(TimeSpan every, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(every, stop).ConfigureAwait(false);
                await _forwards.ReconcileAsync(NothingScanned, NothingAllowed, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or InvalidOperationException)
            {
                _log($"tunnel check failed: {e.Message}");
            }
        }
    }

    private static Task<SshResult> NoMaster(IReadOnlyList<string> arguments, CancellationToken ct) =>
        Task.FromResult(new SshResult(127, string.Empty, "an attach client forwards through tunnels, not a control socket"));
}
