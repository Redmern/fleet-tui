using Fleet.Ports.Forge.Enums;

namespace Fleet.Ports.Forge;

public interface IForgeProbe
{
    Task<ForgeProbeResult> ProbeAsync(string host, CancellationToken ct = default);
}
