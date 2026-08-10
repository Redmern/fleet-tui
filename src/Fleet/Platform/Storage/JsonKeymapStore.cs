using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Models;

namespace Fleet.Platform.Storage;

public sealed class JsonKeymapStore : IKeymapStore
{
    public KeymapConfig Load()
    {
        try
        {
            var file = JsonSerializer.Deserialize(
                File.ReadAllText(FleetPaths.KeymapFile),
                FleetJsonContext.Default.KeymapFile);

            if (file is null)
            {
                return KeymapConfig.Default;
            }

            var bindings = new Dictionary<FleetAction, string>();

            foreach (var (name, key) in file.Bindings)
            {
                if (Enum.TryParse<FleetAction>(name, ignoreCase: true, out var action))
                {
                    bindings[action] = key;
                }
            }

            return new KeymapConfig(file.Prefix, bindings).MergedOverDefaults();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return KeymapConfig.Default;
        }
    }

    public void Save(KeymapConfig config)
    {
        FleetPaths.EnsureDirs();

        var file = new KeymapFile
        {
            Prefix = config.Prefix,
            Bindings = KeymapDiff.AgainstDefaults(config.Bindings)
                .ToDictionary(b => b.Key.ToString(), b => b.Value),
        };

        File.WriteAllText(
            FleetPaths.KeymapFile,
            JsonSerializer.Serialize(file, FleetJsonContext.Default.KeymapFile));
    }
}
