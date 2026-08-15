using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;

namespace Fleet.Ports.Settings;

public interface ISettingsSync
{
    Result Resync(string project, string projectRoot, SettingsConfig config);
}
