using Fleet.Shared;

namespace Fleet.Platform.Storage;

public static class FleetPaths
{
    public const string OverrideVariable = FleetHome.OverrideVariable;

    public static string Config => FleetHome.Config;

    public static string Projects => Path.Combine(Config, "projects");

    public static string Sessions => Path.Combine(Config, "sessions");

    public static string Requests => Path.Combine(Config, "requests");

    public static string LogFile => Path.Combine(Config, "fleet.log");

    public static string KeymapFile => Path.Combine(Config, "keybinds.json");

    public static string Settings => Path.Combine(Config, "settings");

    public static string Approvals => Path.Combine(Config, "approvals");

    public static string Notices => Path.Combine(Config, "notices");

    public static string WindowSessions => Path.Combine(Config, "window-sessions");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(Projects);
        Directory.CreateDirectory(Sessions);
        Directory.CreateDirectory(Settings);
    }
}
