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

    public Result<ThemePalette> SaveCustom(string name, string title, string baseName, ThemePalette palette)
    {
        var key = ThemeNames.Normalize(name);

        if (key.Length == 0)
        {
            return Result<ThemePalette>.Fail("a theme needs a name with at least one letter or digit");
        }

        if (BuiltInThemes.Find(key) is not null)
        {
            return Result<ThemePalette>.Fail($"'{key}' is a built-in theme; pick another name");
        }

        if (key == OmarchyTheme.CustomName)
        {
            return Result<ThemePalette>.Fail($"'{key}' is reserved for '{SyncVerb}', which would overwrite it; pick another name");
        }

        if (Find(baseName) is not { } basis)
        {
            return Result<ThemePalette>.Fail($"unknown base theme '{baseName}'");
        }

        if (basis.Name == key)
        {
            return Result<ThemePalette>.Fail($"'{key}' cannot inherit from itself; pick another base");
        }

        var titled = palette with { Title = string.IsNullOrWhiteSpace(title) ? name.Trim() : title.Trim() };

        store.SaveCustom(key, ThemeToml.WriteOverrides(titled, basis));

        return store.Custom().FirstOrDefault(t => t.Name == key) is { } saved
            ? Result<ThemePalette>.Ok(saved)
            : Result<ThemePalette>.Fail($"'{key}' was written but could not be read back");
    }

    public string BaseOf(string name)
    {
        var key = ThemeNames.Normalize(name);
        var inherits = store.CustomText(key) is { } text
            ? ThemeToml.Parse(text).GetValueOrDefault("inherits")
            : null;

        return inherits is not null && Find(inherits) is { } basis && basis.Name != key
            ? basis.Name
            : BuiltInThemes.DefaultName;
    }

    public Result<ThemePalette> DeleteCustom(string name)
    {
        var key = ThemeNames.Normalize(name);

        if (!store.Custom().Any(t => t.Name == key))
        {
            return Result<ThemePalette>.Fail(BuiltInThemes.Find(key) is not null
                ? $"'{key}' is a built-in theme and cannot be deleted"
                : $"no custom theme '{name}'");
        }

        var active = store.CurrentName() is { } current && ThemeNames.Normalize(current) == key;

        store.DeleteCustom(key);

        if (active)
        {
            store.SaveCurrent(BuiltInThemes.CatppuccinMocha.Name);
        }

        return Result<ThemePalette>.Ok(Active());
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
