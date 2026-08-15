namespace Fleet.Platform.Storage;

public static class FleetPaths
{
    public const string OverrideVariable = "FLEET_CONFIG_HOME";

    public static string Config
    {
        get
        {
            var over = Environment.GetEnvironmentVariable(OverrideVariable);
            if (!string.IsNullOrWhiteSpace(over))
            {
                return over;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "fleet");
        }
    }

    public static string Projects => Path.Combine(Config, "projects");

    public static string Sessions => Path.Combine(Config, "sessions");

    public static string Requests => Path.Combine(Config, "requests");

    public static string LogFile => Path.Combine(Config, "fleet.log");

    public static string KeymapFile => Path.Combine(Config, "keybinds.json");

    public static string Settings => Path.Combine(Config, "settings");

    public static string Approvals => Path.Combine(Config, "approvals");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(Projects);
        Directory.CreateDirectory(Sessions);
        Directory.CreateDirectory(Settings);
    }
}
