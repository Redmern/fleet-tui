using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Settings;
using Fleet.Shared.Settings;

namespace Fleet.Platform.Storage;

public sealed class JsonMachineSettingsStore(string? file = null) : IMachineSettingsStore
{
    private string Location => file ?? FleetPaths.MachineSettingsFile;

    public bool LoadIso()
    {
        try
        {
            if (!File.Exists(Location))
            {
                return SettingsDefaults.Iso;
            }

            return JsonSerializer.Deserialize(File.ReadAllText(Location), FleetJsonContext.Default.MachineSettingsFile)
                is { } stored
                ? stored.Iso ?? SettingsDefaults.Iso
                : true;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void SaveIso(bool on)
    {
        var stored = Load();
        stored.Iso = on == SettingsDefaults.Iso ? null : on;

        var folder = Path.GetDirectoryName(Location);

        if (folder is { Length: > 0 })
        {
            Directory.CreateDirectory(folder);
        }

        var temporary = Location + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored, FleetJsonContext.Default.MachineSettingsFile));
        File.Move(temporary, Location, overwrite: true);
    }

    private MachineSettingsFile Load()
    {
        try
        {
            if (File.Exists(Location)
                && JsonSerializer.Deserialize(File.ReadAllText(Location), FleetJsonContext.Default.MachineSettingsFile)
                    is { } stored)
            {
                return stored;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new MachineSettingsFile();
    }
}
