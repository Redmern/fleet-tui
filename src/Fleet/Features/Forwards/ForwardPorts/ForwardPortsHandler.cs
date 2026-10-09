using Fleet.Features.Forwards.ForwardPorts.Enums;
using Fleet.Features.Forwards.ForwardPorts.Models;
using Fleet.Ports.Browser;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Remotes;
using Fleet.Shared.Results;

namespace Fleet.Features.Forwards.ForwardPorts;

public sealed class ForwardPortsHandler(IPortForwards forwards, IBrowserLauncher browser, IKnownRemoteStore known)
{
    public const string Pitfalls =
        "If the app checks the Host header (Vite allowedHosts) allow localhost; a remapped local port breaks HMR "
        + "unless its clientPort matches; projects on the same localhost share cookies; a docker -p port binds 0.0.0.0 "
        + "on the remote.";

    public async Task<Result<string>> HandleAsync(ForwardOrder order, CancellationToken ct = default)
    {
        try
        {
            var host = order.Host is { } named ? Resolve(named) : string.Empty;
            switch (order.Verb)
            {
                case ForwardVerb.List:
                    var rows = await forwards.ListAsync(ct).ConfigureAwait(false);
                    return Result<string>.Ok(rows.Count == 0
                        ? "no forwarded or detected ports; connect a remote machine first"
                        : string.Join('\n', rows.Select(r => r.Describe())));

                case ForwardVerb.Add:
                    return await OpenedAsync(
                        await forwards.AddAsync(host, order.Port, order.Local, ct).ConfigureAwait(false), order.Open, ct)
                        .ConfigureAwait(false);

                case ForwardVerb.Remove:
                    await forwards.RemoveAsync(host, order.Port, ct).ConfigureAwait(false);
                    return Result<string>.Ok($"stopped forwarding {order.Port} from {host}");

                case ForwardVerb.Start:
                    return await OpenedAsync(
                        await forwards.StartStackAsync(host, order.Project!, ct).ConfigureAwait(false), order.Open, ct)
                        .ConfigureAwait(false);

                case ForwardVerb.Open:
                    return await OpenPortAsync(order.Port, order.Host is null ? null : host, ct).ConfigureAwait(false);

                case ForwardVerb.Stop:
                    await forwards.StopStackAsync(host, order.Project!, ct).ConfigureAwait(false);
                    return Result<string>.Ok($"stopped {order.Project} on {host}");
            }
        }
        catch (MuxUnavailableException e)
        {
            return Result<string>.Fail(e.Message);
        }

        return Result<string>.Fail(ForwardOrders.Usage);
    }

    public async Task<Result<string>> OpenedAsync(PortForward forward, bool open, CancellationToken ct)
    {
        if (forward.Url is null)
        {
            return forward.State == Ports.Forwards.Enums.ForwardState.Failed
                ? Result<string>.Fail(forward.Describe())
                : Result<string>.Ok(forward.Describe());
        }

        if (open && await ForwardOpening.OpenAsync(forwards, browser, forward, ct).ConfigureAwait(false) is { } failed)
        {
            return Result<string>.Fail($"{forward.Describe()}\ncould not open the browser: {failed}");
        }

        return Result<string>.Ok($"{forward.Describe()}\n{forward.Url}");
    }

    public async Task<Result<string>> OpenPortAsync(int port, string? host, CancellationToken ct)
    {
        var rows = await forwards.ListAsync(ct).ConfigureAwait(false);
        var row = rows.FirstOrDefault(r => r.RemotePort == port && r.Url is not null
                                           && (host is null || string.Equals(r.Host, host, StringComparison.OrdinalIgnoreCase)))
            ?? rows.FirstOrDefault(r => host is null && r.LocalPort == port && r.Url is not null);

        return row is null
            ? Result<string>.Fail($"port {port} is not forwarded{(host is null ? string.Empty : $" from {host}")}")
            : await OpenedAsync(row, open: true, ct).ConfigureAwait(false);
    }

    public string Resolve(string host) =>
        known.Load().FirstOrDefault(k => string.Equals(k.Nickname, host, StringComparison.OrdinalIgnoreCase))?.Host ?? host;
}
