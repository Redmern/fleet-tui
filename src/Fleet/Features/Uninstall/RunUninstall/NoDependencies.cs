using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Features.Uninstall.RunUninstall;

public sealed class NoDependencies : IDependencyManifest, IPackageRemover
{
    public static readonly NoDependencies Instance = new();

    public IReadOnlyList<InstalledDependency> Read() => [];

    public DependencyRemoval Remove(InstalledDependency dependency) => new(false, "no package remover");
}
