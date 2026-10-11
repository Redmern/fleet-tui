using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings.Models;

namespace Fleet.Ports.Orchestrations;

public static class ModelRouterExtensions
{
    public static async Task<RoleModel?> TryChooseAsync(
        this IModelRouter? router, ModelRoutingRequest request, IFleetLog? log, CancellationToken ct = default)
    {
        if (router is null)
        {
            return null;
        }

        try
        {
            return await router.ChooseAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log?.Swallowed(e);
            return null;
        }
    }
}
