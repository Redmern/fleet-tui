namespace Fleet.Platform.Mux.WezTerm;

public static class CwdUrl
{
    public static string Normalize(string cwd)
    {
        var s = cwd.Replace('\\', '/');

        if (s.StartsWith("file://", StringComparison.Ordinal))
        {
            s = s["file://".Length..];
        }

        if (s.Length >= 3 && s[0] == '/' && s[2] == ':' && char.IsAsciiLetter(s[1]))
        {
            s = s[1..];
        }

        return s;
    }
}
