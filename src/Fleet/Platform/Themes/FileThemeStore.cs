using Fleet.Platform.Storage;
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
        string[] files;

        try
        {
            files = Directory.Exists(ThemesDirectory)
                ? [.. Directory.EnumerateFiles(ThemesDirectory, "*" + CustomTheme.Extension).Order()]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var themes = new List<ThemePalette>();

        foreach (var file in files)
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

    public static void WriteAtomically(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var temp = $"{file}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";

        File.WriteAllText(temp, text);

        var moved = BusyFiles.Retry(() =>
        {
            File.Move(temp, file, overwrite: true);
            return file;
        }, BusyFiles.Patience);

        if (moved is null)
        {
            BusyFiles.Retry(() =>
            {
                File.Delete(temp);
                return temp;
            }, TimeSpan.Zero);

            throw new IOException($"{file} could not be replaced (another program may have it open); it was left as it was");
        }
    }
}
