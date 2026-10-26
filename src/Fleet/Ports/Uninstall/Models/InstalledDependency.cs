using Fleet.Ports.Uninstall.Enums;

namespace Fleet.Ports.Uninstall.Models;

public sealed record InstalledDependency(
    DependencyTool Tool,
    PackageManager Manager,
    string Package,
    bool Sudo,
    string InstalledAt)
{
    public string ToolName => Tool.ToString().ToLowerInvariant();

    public string ManagerName => Manager.ToString().ToLowerInvariant();
}
