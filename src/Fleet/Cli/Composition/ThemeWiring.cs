using Fleet.Platform.Storage;
using Fleet.Platform.Themes;
using Fleet.Shared.Themes;

namespace Fleet.Cli.Composition;

public static class ThemeWiring
{
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

                    if (Adapters.ApplyWezTermTheme(theme) is not null)
                    {
                        Adapters.TouchWezTermConfig();
                    }
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
