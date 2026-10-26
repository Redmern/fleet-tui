using Fleet.Ports.Uninstall.Models;

namespace Fleet.Ports.Uninstall;

public interface IUninstallTargets
{
    IReadOnlyList<UninstallItem> Discover();

    IReadOnlyList<string> Refusals();

    void Remove(UninstallItem item);
}
