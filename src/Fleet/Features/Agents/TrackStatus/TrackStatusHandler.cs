using Fleet.Ports.Agents;
using Fleet.Shared.Hooks;

namespace Fleet.Features.Agents.TrackStatus;

public sealed class TrackStatusHandler(IAgentStateStore store)
{
    public async Task<bool> HandleAsync(HookEvent hook, DateTime now, CancellationToken ct = default)
    {
        if (HookStatus.ReportFor(hook, now) is not { } report)
        {
            return false;
        }

        await store.ReportAsync(report, ct).ConfigureAwait(false);
        return true;
    }
}
