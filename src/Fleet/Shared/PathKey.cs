namespace Fleet.Shared;

public static class PathKey
{
    public static string For(string path)
    {
        if (path.Length == 0)
        {
            return string.Empty;
        }

        var expanded = HomePath.Expand(path);

        var unified = OperatingSystem.IsWindows()
            ? expanded.Replace('\\', '/')
            : expanded;

        return unified.Length > 1 ? unified.TrimEnd('/') : unified;
    }

    public static bool Same(string left, string right) =>
        string.Equals(For(left), For(right), Comparison);

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
