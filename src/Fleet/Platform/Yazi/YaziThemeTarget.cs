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
        var wanted = YaziTheme.Generate(theme);

        if (File.Exists(ThemeFile))
        {
            var existing = File.ReadAllText(ThemeFile);

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
