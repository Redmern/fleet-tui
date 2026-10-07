using Fleet.Ports.Themes;
using Fleet.Ports.Themes.Models;
using Fleet.Shared.Themes;

namespace Fleet.Features.Themes.ApplyTheme;

public sealed class ApplyThemeHandler(IReadOnlyList<IThemeTarget> targets)
{
    public IReadOnlyList<ThemeApplied> Handle(ThemePalette theme) =>
        [.. targets.Select(t => ApplyOne(t, theme))];

    private static ThemeApplied ApplyOne(IThemeTarget target, ThemePalette theme)
    {
        try
        {
            return target.Apply(theme);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ThemeApplied.Failed(target.Tool, e.Message);
        }
    }
}
