using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Mux.Exceptions;

namespace Fleet.Platform.Mux.Embedded;

public sealed class EmbeddedForwards(Func<EmbeddedDriver> driver) : IPortForwards
{
    public async Task<IReadOnlyList<PortForward>> ListAsync(CancellationToken ct = default)
    {
        using var fleetd = driver();
        try
        {
            return [.. (await fleetd.ForwardsAsync(ct).ConfigureAwait(false)).Select(ForwardHub.FromDto)];
        }
        catch (MuxUnavailableException)
        {
            return [];
        }
    }

    public async Task<PortForward> AddAsync(string host, int remotePort, int? localPort = null, CancellationToken ct = default)
    {
        using var fleetd = driver();
        return await fleetd.AddForwardAsync(host, remotePort, localPort, ct).ConfigureAwait(false) is { } added
            ? ForwardHub.FromDto(added)
            : throw new MuxUnavailableException($"fleetd did not forward {remotePort}");
    }

    public async Task RemoveAsync(string host, int remotePort, CancellationToken ct = default)
    {
        using var fleetd = driver();
        await fleetd.RemoveForwardAsync(host, remotePort, ct).ConfigureAwait(false);
    }

    public async Task<PortForward> StartStackAsync(string host, string project, CancellationToken ct = default)
    {
        using var fleetd = driver();
        return await fleetd.StartStackAsync(host, project, ct).ConfigureAwait(false) is { } up
            ? ForwardHub.FromDto(up)
            : throw new MuxUnavailableException($"fleetd did not start {project}");
    }

    public async Task StopStackAsync(string host, string project, CancellationToken ct = default)
    {
        using var fleetd = driver();
        await fleetd.StopStackAsync(host, project, ct).ConfigureAwait(false);
    }

    public async Task OpenOnViewerAsync(int remotePort, CancellationToken ct = default)
    {
        using var fleetd = driver();
        await fleetd.ViewerOpenAsync(remotePort, ct).ConfigureAwait(false);
    }
}
