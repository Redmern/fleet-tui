namespace Fleet.Platform.Yazi;

public static class YaziConfigHome
{
    public const string OverrideVariable = "YAZI_CONFIG_HOME";

    public static string Resolve() =>
        Resolve(
            Environment.GetEnvironmentVariable(OverrideVariable),
            OperatingSystem.IsWindows(),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string Resolve(string? own, bool windows, string appData, string home) =>
        !string.IsNullOrWhiteSpace(own) ? own.Trim()
        : windows ? Path.Combine(appData, "yazi", "config")
        : Path.Combine(home, ".config", "yazi");
}
