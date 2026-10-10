using Fleet.Ports.Browser;
using Fleet.Ports.Forwards.Models;

namespace Fleet.Ports.Forwards;

public static class ForwardOpening
{
    public static PortForward? Find(IReadOnlyList<PortForward> rows, int port, string? host) =>
        rows.FirstOrDefault(r => r.RemotePort == port && r.Url is not null
                                 && (host is null || string.Equals(r.Host, host, StringComparison.OrdinalIgnoreCase)))
        ?? rows.FirstOrDefault(r => host is null && r.LocalPort == port && r.Url is not null);

    public static async Task<string?> OpenAsync(
        IPortForwards forwards, IBrowserLauncher browser, PortForward forward, CancellationToken ct = default)
    {
        if (forward.Url is not { } url)
        {
            return $"port {forward.RemotePort} is not forwarded ({forward.Error ?? forward.State.ToString().ToLowerInvariant()})";
        }

        if (!forward.Viewer)
        {
            return browser.Open(url);
        }

        try
        {
            await forwards.OpenOnViewerAsync(forward.RemotePort, ct).ConfigureAwait(false);
            return null;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException
                                      or Mux.Exceptions.MuxUnavailableException)
        {
            return e.Message;
        }
    }
}
