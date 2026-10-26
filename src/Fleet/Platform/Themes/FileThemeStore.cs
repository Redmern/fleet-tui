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
        var texts = new Dictionary<string, (string Stem, string Text)>(StringComparer.Ordinal);

        foreach (var file in CustomFiles())
        {
            try
            {
                var stem = Path.GetFileNameWithoutExtension(file);

                texts.TryAdd(ThemeNames.Normalize(stem), (stem, File.ReadAllText(file)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        var resolved = new Dictionary<string, ThemePalette>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        return [.. texts.Keys.Select(Resolve).OfType<ThemePalette>()];

        ThemePalette? Resolve(string name)
        {
            var key = ThemeNames.Normalize(name);

            if (resolved.TryGetValue(key, out var done))
            {
                return done;
            }

            if (!texts.TryGetValue(key, out var source) || !resolving.Add(key))
            {
                return null;
            }

            var theme = CustomTheme.FromToml(source.Stem, source.Text, Resolve);

            resolving.Remove(key);
            resolved[key] = theme;

            return theme;
        }
    }

    public string SaveCustom(ThemePalette theme) => SaveCustom(theme.Name, ThemeToml.Write(theme));

    public string SaveCustom(string name, string toml)
    {
        var file = Path.Combine(ThemesDirectory, name + CustomTheme.Extension);

        WriteAtomically(file, toml);

        return file;
    }

    public string? CustomText(string name)
    {
        var key = ThemeNames.Normalize(name);

        foreach (var file in CustomFiles().Where(f => ThemeNames.Normalize(Path.GetFileNameWithoutExtension(f)) == key))
        {
            try
            {
                return File.ReadAllText(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    public bool DeleteCustom(string name)
    {
        var key = ThemeNames.Normalize(name);
        var deleted = false;

        foreach (var file in CustomFiles().Where(f => ThemeNames.Normalize(Path.GetFileNameWithoutExtension(f)) == key))
        {
            var gone = BusyFiles.Retry(() =>
            {
                File.Delete(file);
                return file;
            }, BusyFiles.Patience);

            if (gone is null)
            {
                throw new IOException($"{file} could not be deleted (another program may have it open)");
            }

            deleted = true;
        }

        return deleted;
    }

    private string[] CustomFiles()
    {
        try
        {
            return Directory.Exists(ThemesDirectory)
                ? [.. Directory.EnumerateFiles(ThemesDirectory, "*" + CustomTheme.Extension).Order()]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
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
