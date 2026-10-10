using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Settings;
using Fleet.Shared.Iso.Models;

namespace Fleet.Platform.Storage;

public sealed class JsonIsoMode : IIsoMode
{
    private static string Target => FleetPaths.IsoFile;

    public IsoConfig Load()
    {
        string text;

        try
        {
            if (!File.Exists(Target))
            {
                return IsoConfig.Off;
            }

            text = File.ReadAllText(Target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return IsoConfig.Unreadable;
        }

        try
        {
            var read = JsonSerializer.Deserialize(text, FleetJsonContext.Default.IsoFile);

            return read is null
                ? IsoConfig.Unreadable
                : new IsoConfig(
                    read.On,
                    [.. (read.AttachFrom ?? []).Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim())],
                    new Dictionary<string, string>(read.Codes ?? [], StringComparer.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return IsoConfig.Unreadable;
        }
    }

    public void Save(IsoConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);

        var saved = new IsoFile
        {
            On = config.On,
            AttachFrom = [.. config.AttachFrom],
            Codes = new Dictionary<string, string>(config.Codes),
        };

        var temp = Target + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(saved, FleetJsonContext.Default.IsoFile));
        File.Move(temp, Target, overwrite: true);
    }
}
