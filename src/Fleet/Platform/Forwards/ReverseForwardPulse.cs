using Fleet.Platform.Forwards.Models;

namespace Fleet.Platform.Forwards;

public sealed class ReverseForwardPulse(
    string localSocket,
    string name,
    Func<IReadOnlyList<string>, CancellationToken, Task<SshResult>> ssh,
    Action<string> log,
    TimeSpan? within = null)
{
    private readonly TimeSpan _within = within ?? SshProcess.Patience;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string ControlPath, string Remote)> _forwarded = new(StringComparer.OrdinalIgnoreCase);

    public string LocalSocket => localSocket;

    public string Name => name;

    public static ReverseForwardPulse? ForViewer(
        Func<string, string?> env,
        Func<string, bool> exists,
        bool unix,
        string machine,
        string user,
        Func<IReadOnlyList<string>, CancellationToken, Task<SshResult>> ssh,
        Action<string> log)
    {
        if (!ReverseForward.OptedIn(env))
        {
            return null;
        }

        if (!unix)
        {
            log($"{ReverseForward.OptInVariable} is set, but voice forwarding needs ssh control sockets, which this system lacks");
            return null;
        }

        if (ReverseForward.LocalPulse(env, exists) is not { } socket)
        {
            log($"{ReverseForward.OptInVariable} is set, but no usable pulse socket was found; set {ReverseForward.PulseVariable}=unix:<socket> to a path without ':'");
            return null;
        }

        return new ReverseForwardPulse(socket, ReverseForward.SocketName(machine, user), ssh, log);
    }

    public async Task OpenAsync(string host, string? controlPath, CancellationToken ct)
    {
        if (controlPath is null)
        {
            log($"remote {host}: no voice forward, it needs an ssh control socket");
            return;
        }

        (string ControlPath, string Remote)? before;
        lock (_gate)
        {
            before = _forwarded.Remove(host, out var known) ? known : null;
        }

        if (before is { } previous)
        {
            await RunAsync(host, SshControl.CancelSocket(previous.ControlPath, host, previous.Remote, localSocket), ct).ConfigureAwait(false);
        }

        if (await RunAsync(host, SshControl.Run(controlPath, host, ReverseForward.Prepare(name)), ct).ConfigureAwait(false) is not { } prepared)
        {
            return;
        }

        if (!prepared.Ok || ReverseForward.Folder(prepared.Output) is not { } directory)
        {
            var said = prepared.Ok ? $"it printed no usable folder: {prepared.Output.Trim()}" : Why(prepared);
            log($"remote {host}: no voice forward, could not prepare the remote socket folder: {said}");
            return;
        }

        var remote = directory + "/" + name;
        if (await RunAsync(host, SshControl.ForwardSocket(controlPath, host, remote, localSocket), ct).ConfigureAwait(false) is not { } forwarded)
        {
            return;
        }

        if (forwarded.Ok)
        {
            lock (_gate)
            {
                _forwarded[host] = (controlPath, remote);
            }
        }

        log(forwarded.Ok
            ? $"remote {host}: forwarded the pulse socket {localSocket} to {remote}"
            : $"remote {host}: could not forward the pulse socket to {remote}: {Why(forwarded)}");
    }

    private async Task<SshResult?> RunAsync(string host, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(_within);
        try
        {
            return await ssh(arguments, bounded.Token).WaitAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log($"remote {host}: no voice forward, ssh did not answer within {_within.TotalSeconds:0.#}s");
            return null;
        }
    }

    private static string Why(SshResult result) =>
        result.Errors.Trim() is { Length: > 0 } said ? said : $"ssh exited with {result.Exit}";
}
