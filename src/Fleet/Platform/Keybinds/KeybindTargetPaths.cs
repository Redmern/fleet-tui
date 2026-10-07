using Fleet.Platform.Claude;
using Fleet.Platform.Keybinds.Models;
using Fleet.Platform.Nvim;
using Fleet.Platform.Storage;

namespace Fleet.Platform.Keybinds;

public static class KeybindTargetPaths
{
    public static string UserModule => Path.Combine(FleetPaths.Config, NvimKeybinds.UserModuleFile);

    public static KeybindTargets Resolve(IEnumerable<string> folders, string home) =>
        new(FleetNvimConfig.Directory, UserModule, Deferred(folders, Path.Combine(home, ".claude")));

    public static IReadOnlyList<string> ClaudeHomes(IEnumerable<string> folders, string defaultHome) =>
        [.. Deferred(folders, defaultHome)];

    private static IEnumerable<string> Deferred(IEnumerable<string> folders, string defaultHome) =>
        folders
            .Select(folder => Normalized(ClaudeConfigHome.ForFolder(folder, defaultHome)))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static string Normalized(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
