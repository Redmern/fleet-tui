using Fleet.Ports.Browser;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;

namespace Fleet.Tests.Features.Forwards;

public sealed class FakeForwards : IPortForwards
{
    public List<PortForward> Rows { get; } = [];

    public List<string> Calls { get; } = [];

    public Task<IReadOnlyList<PortForward>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PortForward>>([.. Rows]);

    public Task<PortForward> AddAsync(string host, int remotePort, int? localPort = null, CancellationToken ct = default)
    {
        Calls.Add($"add {host} {remotePort} {localPort}");
        var row = new PortForward(host, remotePort, localPort ?? remotePort, ForwardState.Forwarded);
        Rows.Add(row);
        return Task.FromResult(row);
    }

    public Task RemoveAsync(string host, int remotePort, CancellationToken ct = default)
    {
        Calls.Add($"rm {host} {remotePort}");
        Rows.RemoveAll(r => r.Host == host && r.RemotePort == remotePort);
        return Task.CompletedTask;
    }

    public Task<PortForward> StartStackAsync(string host, string project, CancellationToken ct = default)
    {
        Calls.Add($"start {host} {project}");
        return Task.FromResult(new PortForward(host, 5173, 5173, ForwardState.Forwarded, project));
    }

    public Task StopStackAsync(string host, string project, CancellationToken ct = default)
    {
        Calls.Add($"stop {host} {project}");
        return Task.CompletedTask;
    }

    public Task OpenOnViewerAsync(int remotePort, CancellationToken ct = default)
    {
        Calls.Add($"viewer-open {remotePort}");
        return Task.CompletedTask;
    }
}

public sealed class FakeBrowser : IBrowserLauncher
{
    public List<string> Opened { get; } = [];

    public string? Fails { get; set; }

    public string? Open(string url)
    {
        if (Fails is { } why)
        {
            return why;
        }

        Opened.Add(url);
        return null;
    }
}
