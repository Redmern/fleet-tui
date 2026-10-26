using Fleet.Ports.Uninstall.Models;

namespace Fleet.Ports.Uninstall;

public interface IDependencyManifest
{
    IReadOnlyList<InstalledDependency> Read();
}
