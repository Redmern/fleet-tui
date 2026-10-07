using Fleet.Ports.Themes;
using Fleet.Shared.Themes;

namespace Fleet.Platform.Themes;

public sealed class FileThemeStore(string config) : IThemeStore
{
    public const string CurrentFileName = "current-theme";

    public const string ThemesDirectoryName = "themes";

    public string CurrentFile => Path.Combine(config, CurrentFileName);

    public string ThemesDirectory => Path.Combine(config, ThemesDirectoryName);

    public string? CurrentName()
    {
        try
        {
            return File.ReadLines(CurrentFile)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SaveCurrent(string name) => WriteAtomically(CurrentFile, name + "\n");

    public IReadOnlyList<ThemePalette> Custom()
    {
        if (!Directory.Exists(ThemesDirectory))
        {
            return [];
        }

        var themes = new List<ThemePalette>();

        foreach (var file in Directory.EnumerateFiles(ThemesDirectory, "*" + CustomTheme.Extension).Order())
        {
            try
            {
                themes.Add(CustomTheme.FromToml(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return themes;
    }

    public string SaveCustom(ThemePalette theme)
    {
        var file = Path.Combine(ThemesDirectory, theme.Name + CustomTheme.Extension);

        WriteAtomically(file, ThemeToml.Write(theme));

        return file;
    }

    private static void WriteAtomically(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var temp = file + ".tmp";

        File.WriteAllText(temp, text);
        File.Move(temp, file, overwrite: true);
    }
}
