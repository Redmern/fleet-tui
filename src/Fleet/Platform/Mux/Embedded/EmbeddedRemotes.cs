using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Platform.Mux.Embedded;

public sealed class EmbeddedRemotes(Func<EmbeddedDriver> driver) : IRemoteMachines
{
    public async Task<IReadOnlyList<RemoteMachine>> ListAsync(CancellationToken ct = default)
    {
        using var fleetd = driver();
        try
        {
            return [.. (await fleetd.RemotesAsync(ct).ConfigureAwait(false)).Select(ToMachine)];
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            return [];
        }
    }

    public async Task ConnectAsync(string host, CancellationToken ct = default)
    {
        using var fleetd = driver();
        await fleetd.ConnectRemoteAsync(host, ct).ConfigureAwait(false);
    }

    public async Task AnswerAsync(string host, string answer, CancellationToken ct = default)
    {
        using var fleetd = driver();
        await fleetd.AnswerRemoteAsync(host, answer, ct).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(string host, CancellationToken ct = default)
    {
        using var fleetd = driver();
        await fleetd.DisconnectRemoteAsync(host, ct).ConfigureAwait(false);
    }

    public async Task OpenInNewWindowAsync(string host, string project, CancellationToken ct = default)
    {
        using var fleetd = driver();
        await fleetd.OpenRemoteWindowAsync(host, project, ct).ConfigureAwait(false);
    }

    public static RemoteMachine ToMachine(RemoteDto dto) =>
        new(
            dto.Host,
            dto.Name.Length > 0 ? dto.Name : dto.Host,
            dto.State switch
            {
                RemoteLink.Asking => RemoteState.Asking,
                RemoteLink.Connected => RemoteState.Connected,
                RemoteLink.Failed => RemoteState.Failed,
                _ => RemoteState.Connecting,
            },
            dto.Projects,
            dto.Error,
            dto.Prompt,
            dto.Secret);
}