using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Ports.Mux.Exceptions;

namespace Fleet.Cli.Composition;

public static class WarmMenuWiring
{
    public static async Task<string?> WaitForOpenAsync(CancellationToken ct = default)
    {
        if (Environment.GetEnvironmentVariable(FleetDaemon.PaneVariable) is not { Length: > 0 }
            || Environment.GetEnvironmentVariable(FleetDaemon.ClientVariable) is { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var driver = new EmbeddedDriver(Endpoint.Default());
            return await driver.WaitForMenuOpenAsync(driver.CurrentPane, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is MuxUnavailableException or OperationCanceledException or IOException)
        {
            return null;
        }
    }
}
