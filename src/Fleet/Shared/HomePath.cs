namespace Fleet.Shared;

/// <summary>
/// Stores paths under the home directory as "~/..." so a saved project is
/// portable between machines and user profiles.
/// </summary>
public static class HomePath
{
    public static string Contract(string path)
    {
        var home = Home();
        if (string.IsNullOrEmpty(home))
        {
            return path;
        }

        var clean = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var cleanHome = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));

        if (string.Equals(clean, cleanHome, Comparison))
        {
            return "~";
        }

        var prefix = cleanHome + Path.DirectorySeparatorChar;

        return clean.StartsWith(prefix, Comparison)
            ? "~" + Path.DirectorySeparatorChar + clean[prefix.Length..]
            : clean;
    }

    public static string Expand(string path)
    {
        // Only a leading "~" followed by a separator counts, so a directory
        // genuinely named "~backup" is left alone.
        if (path != "~" && !path.StartsWith("~/") && !path.StartsWith("~\\"))
        {
            return path;
        }

        var home = Home();
        if (string.IsNullOrEmpty(home))
        {
            return path;
        }

        return path == "~" ? home : Path.Combine(home, path[2..]);
    }

    // Windows paths are case-insensitive; Linux paths are not.
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
