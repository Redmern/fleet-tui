using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings.Models;

namespace Fleet.Ports.Orchestrations;

public interface IModelRouter
{
    Task<RoleModel?> ChooseAsync(ModelRoutingRequest request, CancellationToken ct = default);
}
