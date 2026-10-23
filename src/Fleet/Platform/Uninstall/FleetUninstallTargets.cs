using System.Runtime.InteropServices;
using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Platform.Uninstall;

public sealed class FleetUninstallTargets(string executable) : IUninstallTargets
{
    public IReadOnlyList<UninstallItem> Discover() =>
        File.Exists(executable) ? [new UninstallItem(UninstallKind.Binary, executable, "the fleet binary")] : [];

    public void Remove(UninstallItem item)
    {
        switch (item.Kind)
        {
            case UninstallKind.Binary:
                RemoveBinary(item.Path);
                break;
        }
    }

    private static void RemoveBinary(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.Move(path, $"{path}.old-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
            return;
        }

        File.Delete(path);
    }
}
