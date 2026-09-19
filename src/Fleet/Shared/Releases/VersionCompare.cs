namespace Fleet.Shared.Releases;

public static class VersionCompare
{
    public static bool IsNewer(string latestTag, string currentVersion)
    {
        var latest = Parse(latestTag);

        if (latest is null)
        {
            return false;
        }

        var current = Parse(currentVersion);

        return current is null || latest > current;
    }

    public static bool AreEqual(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);

        return pa is not null && pb is not null && pa == pb;
    }

    private static Version? Parse(string text) =>
        Version.TryParse(text.TrimStart('v', 'V'), out var version) ? version : null;
}
