using Fleet.Features.Themes.ManageThemes.Models;
using Fleet.Ports.Themes;
using Fleet.Shared.Results;
using Fleet.Shared.Themes;

namespace Fleet.Features.Themes.ManageThemes;

public sealed class ManageThemesHandler(IThemeStore store, IOmarchy omarchy)
{
    public const string SyncVerb = "theme sync";

    public const string HookTail = $" {SyncVerb} >/dev/null 2>&1 || true";

    public IReadOnlyList<ThemePalette> List()
    {
        var custom = store.Custom();
        var names = custom.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        return [.. BuiltInThemes.All.Where(t => !names.Contains(t.Name)), .. custom];
    }

    public bool IsCustom(ThemePalette theme) => store.Custom().Any(t => t.Name == theme.Name);

    public ThemePalette Active() =>
        store.CurrentName() is { } name ? Find(name) ?? BuiltInThemes.CatppuccinMocha : BuiltInThemes.CatppuccinMocha;

    public Result<ThemePalette> Set(string name)
    {
        if (Find(name) is not { } theme)
        {
            return Result<ThemePalette>.Fail($"unknown theme '{name}'; 'fleet theme list' shows the choices");
        }

        store.SaveCurrent(theme.Name);

        return Result<ThemePalette>.Ok(theme);
    }

    public Result<ThemePalette> SyncOmarchy()
    {
        if (omarchy.Current() is not { } snapshot)
        {
            return Result<ThemePalette>.Fail("no omarchy theme found (~/.config/omarchy/current/theme)");
        }

        ThemePalette theme;

        try
        {
            theme = OmarchyTheme.Resolve(snapshot);
        }
        catch (InvalidOperationException e)
        {
            return Result<ThemePalette>.Fail(e.Message);
        }

        if (theme.Name == OmarchyTheme.CustomName)
        {
            store.SaveCustom(theme);
        }

        store.SaveCurrent(theme.Name);

        return Result<ThemePalette>.Ok(theme);
    }

    public OmarchySetup InstallOmarchy(string executable)
    {
        var hook = omarchy.InstallHook(HookLine(executable), HookTail);
        var synced = SyncOmarchy();

        return new OmarchySetup(
            hook,
            omarchy.HookFile,
            synced.Succeeded ? synced.Value : null,
            synced.Error);
    }

    public static string HookLine(string executable) =>
        $"'{executable.Replace("'", "'\\''", StringComparison.Ordinal)}'{HookTail}";

    private ThemePalette? Find(string name)
    {
        var key = ThemeNames.Normalize(name);

        return store.Custom().FirstOrDefault(t => t.Name == key) ?? BuiltInThemes.Find(name);
    }
}
