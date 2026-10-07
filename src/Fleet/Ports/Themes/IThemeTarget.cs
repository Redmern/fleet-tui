using Fleet.Ports.Themes.Models;
using Fleet.Shared.Themes;

namespace Fleet.Ports.Themes;

public interface IThemeTarget
{
    string Tool { get; }

    ThemeApplied Apply(ThemePalette theme);
}
