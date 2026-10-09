using Fleet.Ports.Forwards.Models;

namespace Fleet.Ports.Forwards;

public interface IPortForwards
{
    Task<IReadOnlyList<PortForward>> ListAsync(CancellationToken ct = default);

    Task<PortForward> AddAsync(string host, int remotePort, int? localPort = null, CancellationToken ct = default);

    Task RemoveAsync(string host, int remotePort, CancellationToken ct = default);

    Task<PortForward> StartStackAsync(string host, string project, CancellationToken ct = default);

    Task StopStackAsync(string host, string project, CancellationToken ct = default);

    Task OpenOnViewerAsync(int remotePort, CancellationToken ct = default);
}
