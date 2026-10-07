using Fleet.Platform.Themes;
using Fleet.Ports.Themes;
using Fleet.Ports.Themes.Models;
using Fleet.Shared.Themes;

namespace Fleet.Platform.Nvim;

public sealed class NvimThemeTarget(string configDirectory) : IThemeTarget
{
    public string Tool => "nvim";

    public string PaletteFile => Path.Combine(configDirectory, NvimTheme.PaletteFile);

    public ThemeApplied Apply(ThemePalette theme)
    {
        if (!Directory.Exists(configDirectory))
        {
            return ThemeApplied.Skipped(
                Tool, $"fleet's nvim config is not installed in {configDirectory}; fleet writes the palette when it installs it");
        }

        var wanted = NvimTheme.Generate(theme);

        if (File.Exists(PaletteFile) && File.ReadAllText(PaletteFile) == wanted)
        {
            return ThemeApplied.Unchanged(Tool, PaletteFile);
        }

        FileThemeStore.WriteAtomically(PaletteFile, wanted);

        return ThemeApplied.Applied(Tool, $"wrote {PaletteFile}; open fleet nvim panes reload it");
    }
}
