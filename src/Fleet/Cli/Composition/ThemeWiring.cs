using Fleet.Features.Themes.ApplyTheme;
using Fleet.Platform.Claude;
using Fleet.Platform.Nvim;
using Fleet.Platform.Storage;
using Fleet.Platform.Themes;
using Fleet.Platform.Yazi;
using Fleet.Ports.Themes.Models;
using Fleet.Shared.Themes;

namespace Fleet.Cli.Composition;

public static class ThemeWiring
{
    public static IReadOnlyList<ThemeApplied> Apply(ThemePalette theme, string folder) =>
        new ApplyThemeHandler(
        [
            new NvimThemeTarget(FleetNvimConfig.Directory),
            new ClaudeThemeTarget(ClaudeConfigHome.ForFolder(folder, Path.Combine(Adapters.HomeDirectory, ".claude"))),
            new YaziThemeTarget(YaziConfigHome.Resolve()),
        ]).Handle(theme);

    public static IDisposable? Follow(Action<ThemePalette> apply)
    {
        var gate = new Lock();
        var last = ThemeToml.Write(Adapters.Themes().Active());

        try
        {
            return new ThemeWatcher(FleetPaths.Config, () =>
            {
                try
                {
                    var theme = Adapters.Themes().Active();
                    var text = ThemeToml.Write(theme);

                    lock (gate)
                    {
                        if (text == last)
                        {
                            return;
                        }

                        last = text;
                    }

                    apply(theme);
                }
                catch (Exception e)
                {
                    Adapters.Log().Swallowed(e);
                }
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
