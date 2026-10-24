using Fleet.Shared.Themes;

namespace Fleet.Ports.Themes;

public interface IThemeStore
{
    string CurrentFile { get; }

    string? CurrentName();

    void SaveCurrent(string name);

    IReadOnlyList<ThemePalette> Custom();

    string SaveCustom(ThemePalette theme);

    string SaveCustom(string name, string toml);

    string? CustomText(string name);

    bool DeleteCustom(string name);
}
