namespace Fleet.Platform.Mux.WezTerm;

public static class WezTermSockets
{
    public const string Variable = "WEZTERM_UNIX_SOCKET";

    public const string Prefix = "gui-sock-";

    public static string RuntimeDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

            if (!string.IsNullOrWhiteSpace(xdg))
            {
                return Path.Combine(xdg, "wezterm");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "wezterm");
        }
    }

    public static IReadOnlyList<string> Candidates(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(directory, Prefix + "*")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static string? FromEnvironment()
    {
        var current = Environment.GetEnvironmentVariable(Variable);

        return string.IsNullOrWhiteSpace(current) ? null : current;
    }
}
