using Fleet.Ports.Agents;
using Fleet.Shared.Status;

namespace Fleet.Platform.Storage;

public sealed class StatusFileInboxes(IAgentStateStore states) : IAgentInboxes
{
    public async Task<string?> AddressAsync(string folder, CancellationToken ct = default) =>
        AgentStatusRules.InboxOf(await states.GetSnapshotAsync(ct).ConfigureAwait(false), folder);
}
