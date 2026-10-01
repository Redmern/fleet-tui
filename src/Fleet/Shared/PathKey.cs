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

    public static bool Within(string path, string root)
    {
        var inner = For(path);
        var outer = For(root);

        return outer.Length > 0
               && (string.Equals(inner, outer, Comparison) || inner.StartsWith(outer + "/", Comparison));
    }

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
