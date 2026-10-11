using Fleet.Platform.Forwards.Models;

namespace Fleet.Platform.Forwards;

public sealed class ReverseForwardPulse(
    string localSocket,
    string name,
    Func<IReadOnlyList<string>, CancellationToken, Task<SshResult>> ssh,
    Action<string> log)
{
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
            log($"{ReverseForward.OptInVariable} is set, but no pulse socket was found; set {ReverseForward.PulseVariable}=unix:<socket>");
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

        var prepared = await ssh(SshControl.Run(controlPath, host, ReverseForward.Prepare(name)), ct).ConfigureAwait(false);
        if (!prepared.Ok || prepared.Output.Trim() is not { Length: > 0 } directory)
        {
            log($"remote {host}: no voice forward, could not prepare the remote socket folder: {Why(prepared)}");
            return;
        }

        var remote = directory + "/" + name;
        var forwarded = await ssh(SshControl.ForwardSocket(controlPath, host, remote, localSocket), ct).ConfigureAwait(false);
        log(forwarded.Ok
            ? $"remote {host}: forwarded the pulse socket {localSocket} to {remote}"
            : $"remote {host}: could not forward the pulse socket to {remote}: {Why(forwarded)}");
    }

    private static string Why(SshResult result) =>
        result.Errors.Trim() is { Length: > 0 } said ? said : $"ssh exited with {result.Exit}";
}
