using Fleet.Cli.Composition.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Logging;
using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Constants;
using Fleet.Platform.Mux.Models;
using Fleet.Features.Files.BrowseFiles;
using Fleet.Features.Setup.RunSetup;
using Fleet.Features.Setup.RunSetup.Enums;
using Fleet.Features.Setup.RunSetup.Models;
using Fleet.Platform.Mux.WezTerm;
using Fleet.Platform.Harness;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Git;
using Fleet.Ports.Keymap;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Requests;
using Fleet.Ports.Settings;
using Fleet.Ui;

namespace Fleet.Cli.Composition;

public static class Adapters
{
    public static IFleetLog Log() => new FileLog();

    public static IProjectStore Projects() => new JsonProjectStore();

    public static IGitRunner Git() => new GitRunner();

    public static IKeymapStore Keymaps() => new JsonKeymapStore();

    public static ISettingsStore Settings() => new JsonSettingsStore();

    public static ISettingsSync SettingsSync() => new NullSettingsSync();

    public static IActionRequestStore Requests() => new FileActionRequestStore();

    public static IWorkspaceRequestStore Workspaces() => new FileWorkspaceRequestStore();

    public static IAgentStore Agents() => new JsonAgentStore();

    public static MuxSelection Mux(IFleetLog log)
    {
        var chosen = DriverSelector.Choose(MuxEnvironment.Current(MuxEnvironment.OnPath));

        var unsupported = MuxTrouble.With(chosen, OnPath(DriverNames.WezTerm));

        return new MuxSelection(
            new FailSilentDriver(new WezTermDriver(), log.Swallowed), chosen, unsupported);
    }

    public static string ConfigDirectory => FleetPaths.Config;

    public static string Executable => Environment.ProcessPath ?? "fleet";

    public static void MarkDashboardPane(string project) =>
        WezTermUserVars.MarkDashboard(project);

    public static bool OnPath(string exe) => MuxEnvironment.OnPath(exe);

    public static string? PickFolder(IMuxDriver mux, string project, string startIn)
    {
        var file = Path.Combine(
            Path.GetTempPath(), $"fleet-folder-{Guid.NewGuid():N}");

        var pane = mux
            .SpawnAsync(FileBrowser.Choose(startIn, project, CurrentWindow(mux), file))
            .GetAwaiter()
            .GetResult();

        if (pane.IsNone)
        {
            return null;
        }

        mux.SetTitleAsync(pane, FileBrowser.ChooseTitle).GetAwaiter().GetResult();
        mux.FocusPaneAsync(pane).GetAwaiter().GetResult();

        try
        {
            return WaitForChoice(mux, pane, file);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static string? BrowseFolder(IMuxDriver mux, string project, string root)
    {
        var pane = mux
            .SpawnAsync(FileBrowser.Browse(root, project, CurrentWindow(mux)))
            .GetAwaiter()
            .GetResult();

        if (pane.IsNone)
        {
            return null;
        }

        mux.SetTitleAsync(pane, FileBrowser.BrowseTitle).GetAwaiter().GetResult();
        mux.FocusPaneAsync(pane).GetAwaiter().GetResult();

        return root;
    }

    private static string? WaitForChoice(IMuxDriver mux, PaneId pane, string file)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(file))
            {
                return FileBrowser.Chosen(file, ReadOrNull);
            }

            Thread.Sleep(150);

            var panes = mux.ListPanesAsync().GetAwaiter().GetResult();

            if (panes.All(p => p.Id != pane))
            {
                return File.Exists(file) ? FileBrowser.Chosen(file, ReadOrNull) : null;
            }
        }

        return null;
    }

    private static string? ReadOrNull(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? CurrentWindow(IMuxDriver mux) =>
        mux.ListPanesAsync().GetAwaiter().GetResult()
            .FirstOrDefault(p => p.IsActive)?.WindowId;

    public static ConfigWiring WireWezTermConfig()
    {
        var home = Home;

        var config = WezTermWiring.ConfigCandidates(home).FirstOrDefault(File.Exists);

        if (config is null)
        {
            return new ConfigWiring(WiringState.Missing, WezTermWiring.ConfigCandidates(home)[0]);
        }

        try
        {
            var text = File.ReadAllText(config);

            if (WezTermWiring.AlreadyWired(text))
            {
                return new ConfigWiring(WiringState.Already, config);
            }

            var wired = WezTermWiring.Wire(text);

            File.Copy(config, config + ".bak-fleet", overwrite: true);
            File.WriteAllText(config, wired.Text);

            return new ConfigWiring(
                WiringState.Added,
                config,
                wired.BeforeReturn ? string.Empty : "appended at the end of the file");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ConfigWiring(WiringState.Failed, config, e.Message);
        }
    }

    public static string HomeDirectory => Home;

    private static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string WriteKeybindModule(Keymap keymap)
    {
        var target = Path.Combine(WezTermWiring.ModuleDirectory(Home), WezTermWiring.Module);

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
