namespace Fleet.Shared;

public static class PathKey
{
    public static string For(string path) =>
        path.Length == 0
            ? string.Empty
            : Path.TrimEndingDirectorySeparator(HomePath.Expand(path));

    public static bool Same(string left, string right) =>
        string.Equals(For(left), For(right), Comparison);

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
