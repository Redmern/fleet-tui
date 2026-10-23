using Fleet.Ports.Uninstall.Models;

namespace Fleet.Ports.Uninstall;

public interface IUninstallTargets
{
    IReadOnlyList<UninstallItem> Discover();

    void Remove(UninstallItem item);
}
