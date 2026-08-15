using Fleet.Shared.Settings.Models;

namespace Fleet.Ports.Settings;

public interface ISettingsStore
{
    SettingsConfig Load(string project);

    void Save(string project, SettingsConfig config);
}
