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

    public static string KnownRemotesFile => Path.Combine(Config, "remotes.json");

    public static string UpdateCheckFile => Path.Combine(Config, "update-check.json");

    public static string Settings => Path.Combine(Config, "settings");

    public static string HeadSettingsFile => Path.Combine(Config, "head.json");

    public static string MenuSettingsFile => Path.Combine(Config, "menu.json");

    public static string NvimSettingsFile => Path.Combine(Config, "nvim.json");

    public static string MachineSettingsFile => Path.Combine(Config, "machine.json");

    public static string Approvals => Path.Combine(Config, "approvals");

    public static string Notices => Path.Combine(Config, "notices");

    public static string Status => Path.Combine(Config, "status");

    public static string WindowSessions => Path.Combine(Config, "window-sessions");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(Projects);
        Directory.CreateDirectory(Sessions);
        Directory.CreateDirectory(Settings);
    }
}
