namespace Fleet.Platform.Storage;

/// <summary>Where fleet keeps its configuration and per-session state.</summary>
public static class FleetPaths
{
    /// <summary>Set this to relocate everything. Tests rely on it.</summary>
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

            // ApplicationData is %APPDATA% on Windows and honours XDG_CONFIG_HOME
            // (defaulting to ~/.config) on Linux, so one call covers both.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "fleet");
        }
    }

    public static string Projects => Path.Combine(Config, "projects");

    public static string Sessions => Path.Combine(Config, "sessions");

    public static string LogFile => Path.Combine(Config, "fleet.log");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(Projects);
        Directory.CreateDirectory(Sessions);
    }
}
