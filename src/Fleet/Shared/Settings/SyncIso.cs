using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Settings;

public static class SyncIso
{
    public const string MachineRefusal =
        "ISO mode is on for this machine: nothing is sent to another machine. Only fleet iso off, in a terminal on this machine, lifts it.";

    public static bool On(bool machine, SettingsConfig project) => machine || project.Iso;

    public static string? Refused(string project, bool machine, SettingsConfig config) =>
        machine ? MachineRefusal
        : config.Iso ? $"ISO mode is on for {project}: nothing is sent to another machine. Turn it off in that project's settings."
        : null;
}
