namespace Fleet.Ports.Settings;

public interface IMachineSettingsStore
{
    bool LoadIso();

    void SaveIso(bool on);
}
