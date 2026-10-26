using Fleet.Ports.Uninstall.Models;

namespace Fleet.Ports.Uninstall;

public interface IPackageRemover
{
    DependencyRemoval Remove(InstalledDependency dependency);
}
