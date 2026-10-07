using Fleet.Ports.Themes.Enums;
using Fleet.Shared.Themes.Models;

namespace Fleet.Ports.Themes;

public interface IOmarchy
{
    string HookFile { get; }

    OmarchySnapshot? Current();

    HookInstall InstallHook(string line, string marker);
}
