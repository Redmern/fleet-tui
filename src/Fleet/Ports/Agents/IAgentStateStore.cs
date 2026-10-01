using Fleet.Shared.Status.Models;

namespace Fleet.Ports.Agents;

public interface IAgentStateStore
{
    Task ReportAsync(AgentReport report, CancellationToken ct = default);

    Task<AgentSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
