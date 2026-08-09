using Fleet.Cli.Composition.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Logging;
using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Constants;
using Fleet.Platform.Mux.Models;
using Fleet.Platform.Mux.WezTerm;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Git;
using Fleet.Ports.Keymap;
using Fleet.Ports.Projects;
using Fleet.Ports.Requests;
using Fleet.Ui;

namespace Fleet.Cli.Composition;

public static class Adapters
{
    public static IFleetLog Log() => new FileLog();

    public static IProjectStore Projects() => new JsonProjectStore();

    public static IGitRunner Git() => new GitRunner();

    public static IKeymapStore Keymaps() => new JsonKeymapStore();

    public static IActionRequestStore Requests() => new FileActionRequestStore();

    public static IWorkspaceRequestStore Workspaces() => new FileWorkspaceRequestStore();

    public static IAgentStore Agents() => new JsonAgentStore();

    public static MuxSelection Mux(IFleetLog log)
    {
        var chosen = DriverSelector.Choose(MuxEnvironment.Current(MuxEnvironment.OnPath));

        var unsupported = chosen == DriverNames.WezTerm
            ? null
            : $"the '{chosen}' driver is not implemented yet (phase 1 ships wezterm only)";

        return new MuxSelection(
            new FailSilentDriver(new WezTermDriver(), log.Swallowed), chosen, unsupported);
    }

    public static string ConfigDirectory => FleetPaths.Config;

    public static string Executable => Environment.ProcessPath ?? "fleet";

    public static void MarkDashboardPane(string project) =>
        WezTermUserVars.MarkDashboard(project);

    public static string WriteKeybindModule(Keymap keymap)
    {
        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wezterm",
            "fleet.lua");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(
            target,
            WezTermKeybinds.Generate(keymap, Executable, FileWorkspaceRequestStore.File));

        return target;
    }

    public static string TouchWezTermConfig()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] candidates =
        [
            Path.Combine(home, ".wezterm.lua"),
            Path.Combine(home, ".config", "wezterm", "wezterm.lua"),
        ];

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                File.SetLastWriteTimeUtc(candidate, DateTime.UtcNow);
                return $"nudged {candidate}";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return $"could not nudge {candidate}: {e.Message}";
            }
        }

        return "no wezterm config found; reload wezterm yourself";
    }
}
