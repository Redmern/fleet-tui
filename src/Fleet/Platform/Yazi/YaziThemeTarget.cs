using Fleet.Platform.Storage;
using Fleet.Platform.Themes;
using Fleet.Ports.Themes;
using Fleet.Ports.Themes.Models;
using Fleet.Shared.Themes;

namespace Fleet.Platform.Yazi;

public sealed class YaziThemeTarget(string configDirectory) : IThemeTarget
{
    public string Tool => "yazi";

    public string ThemeFile => Path.Combine(configDirectory, YaziTheme.FileName);

    public ThemeApplied Apply(ThemePalette theme)
    {
        if (!Directory.Exists(configDirectory))
        {
            return ThemeApplied.Skipped(Tool, $"no yazi config folder at {configDirectory}; create it to let fleet theme yazi");
        }

        var wanted = YaziTheme.Generate(theme);

        if (File.Exists(ThemeFile))
        {
            if (BusyFiles.Retry(() => File.ReadAllText(ThemeFile), BusyFiles.Patience) is not { } existing)
            {
                return ThemeApplied.Failed(Tool, $"{ThemeFile} could not be read (another program may have it open); fleet left it alone");
            }

            if (!YaziTheme.IsFleets(existing))
            {
                return ThemeApplied.Skipped(
                    Tool, $"{ThemeFile} is your own (no '{YaziTheme.Header}' first line); fleet left it alone");
            }

            if (existing == wanted)
            {
                return ThemeApplied.Unchanged(Tool, ThemeFile);
            }
        }

        FileThemeStore.WriteAtomically(ThemeFile, wanted);

        return ThemeApplied.Applied(Tool, $"wrote {ThemeFile}; yazi picks it up on its next start");
    }
}
