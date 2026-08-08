namespace Fleet.Platform.Mux.WezTerm;

public static class CwdUrl
{
    /// <summary>
    /// Turns WezTerm's file:// working directory into a plain path.
    ///
    /// On Windows the URL is file:///C:/repos/x — three slashes — so trimming the
    /// scheme naively leaves "/C:/repos/x", which git rejects. On Unix the same
    /// URL is file:///home/red/x, where the leading slash IS part of the path and
    /// must be kept. Backslashes are folded to forward slashes so callers split on
    /// one separator.
    /// </summary>
    public static string Normalize(string cwd)
    {
        var s = cwd.Replace('\\', '/');

        if (s.StartsWith("file://", StringComparison.Ordinal))
        {
            s = s["file://".Length..];
        }

        // A Windows drive letter arrives as "/C:/..."; drop the slash the URL form
        // adds. Anything else keeps its leading slash, because on Unix it is root.
        if (s.Length >= 3 && s[0] == '/' && s[2] == ':' && char.IsAsciiLetter(s[1]))
        {
            s = s[1..];
        }

        return s;
    }
}
