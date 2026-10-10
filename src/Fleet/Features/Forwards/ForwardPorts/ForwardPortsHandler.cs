using Fleet.Features.Forwards.ForwardPorts.Enums;
using Fleet.Features.Forwards.ForwardPorts.Models;
using Fleet.Ports.Browser;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Remotes;
using Fleet.Shared.Results;

namespace Fleet.Features.Forwards.ForwardPorts;

public sealed class ForwardPortsHandler(IPortForwards forwards, IBrowserLauncher browser, IKnownRemoteStore known, string user)
{
    public async Task<Result<string>> HandleAsync(ForwardOrder order, CancellationToken ct = default)
    {
        try
        {
            switch (order.Verb)
            {
                case ForwardVerb.List:
                    var rows = await forwards.ListAsync(ct).ConfigureAwait(false);
                    return Result<string>.Ok(rows.Count == 0
                        ? "no forwarded or detected ports; connect a remote machine first"
                        : string.Join('\n', rows.Select(r => r.Describe())));

                case ForwardVerb.Add when order.Host is null:
                    return (await forwards.ForwardToViewerAsync(order.Port, ct).ConfigureAwait(false)).Answer(order.Port, forward: true, user);

                case ForwardVerb.Add:
                    return await OpenedAsync(
                        await forwards.AddAsync(Resolve(order.Host), order.Port, order.Local, ct).ConfigureAwait(false), order.Open, ct)
                        .ConfigureAwait(false);

                case ForwardVerb.Remove when order.Host is null:
                    return (await forwards.UnforwardFromViewerAsync(order.Port, ct).ConfigureAwait(false)).Answer(order.Port, forward: false, user);

                case ForwardVerb.Remove:
                    {
                        var host = Resolve(order.Host);
                        await forwards.RemoveAsync(host, order.Port, ct).ConfigureAwait(false);
                        return Result<string>.Ok($"stopped forwarding {order.Port} from {host}");
                    }

                case ForwardVerb.Start when order.Host is { } host:
                    return await OpenedAsync(
                        await forwards.StartStackAsync(Resolve(host), order.Project!, ct).ConfigureAwait(false), order.Open, ct)
                        .ConfigureAwait(false);

                case ForwardVerb.Open:
                    return await OpenPortAsync(order.Port, order.Host is { } named ? Resolve(named) : null, ct).ConfigureAwait(false);

                case ForwardVerb.Stop when order.Host is { } host:
                    await forwards.StopStackAsync(Resolve(host), order.Project!, ct).ConfigureAwait(false);
                    return Result<string>.Ok($"stopped {order.Project} on {Resolve(host)}");
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
        var row = ForwardOpening.Find(await forwards.ListAsync(ct).ConfigureAwait(false), port, host);

        return row is null
            ? Result<string>.Fail($"port {port} is not forwarded{(host is null ? string.Empty : $" from {host}")}")
            : await OpenedAsync(row, open: true, ct).ConfigureAwait(false);
    }

    public string Resolve(string host) =>
        known.Load().FirstOrDefault(k => string.Equals(k.Nickname, host, StringComparison.OrdinalIgnoreCase))?.Host ?? host;
}
