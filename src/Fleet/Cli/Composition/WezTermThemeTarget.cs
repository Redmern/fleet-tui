using Fleet.Platform.Mux.WezTerm;
using Fleet.Ports.Themes;
using Fleet.Ports.Themes.Models;
using Fleet.Shared.Themes;

namespace Fleet.Cli.Composition;

public sealed class WezTermThemeTarget : IThemeTarget
{
    public string Tool => "wezterm";

    public ThemeApplied Apply(ThemePalette theme)
    {
        var module = Path.Combine(WezTermWiring.ModuleDirectory(Adapters.HomeDirectory), WezTermTheme.Module);

        if (!File.Exists(module))
        {
            return ThemeApplied.Skipped(Tool, $"{module} does not exist; 'fleet setup' installs it");
        }

        return Adapters.ApplyWezTermTheme(theme) is { } lua
            ? ThemeApplied.Applied(Tool, $"wrote {lua}; {Adapters.TouchWezTermConfig()}")
            : ThemeApplied.Unchanged(Tool, module);
    }
}
