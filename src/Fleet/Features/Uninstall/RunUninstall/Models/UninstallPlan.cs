using Fleet.Ports.Uninstall.Models;

namespace Fleet.Features.Uninstall.RunUninstall.Models;

public sealed record UninstallPlan(IReadOnlyList<UninstallItem> Items)
{
    public IReadOnlyList<string> Refused { get; init; } = [];
}
