using Fleet.Shared.Themes;

namespace Fleet.Ports.Themes;

public interface IThemeStore
{
    string CurrentFile { get; }

    string? CurrentName();

    void SaveCurrent(string name);

    IReadOnlyList<ThemePalette> Custom();

    string SaveCustom(ThemePalette theme);
}
