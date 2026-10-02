using System.Text;
using Fleet.Cli.Composition.Models;
using Fleet.Platform.Aidlc;
using Fleet.Platform.Approvals;
using Fleet.Platform.Claude;
using Fleet.Platform.Git;
using Fleet.Platform.Mcp;
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
using Fleet.Platform.Hooks;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Aidlc;
using Fleet.Ports.Approvals;
using Fleet.Ports.Git;
using Fleet.Ports.Mcp;
using Fleet.Ports.Keymap;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Orchestrations;
using Fleet.Ports.Projects;
using Fleet.Ports.Releases;
using Fleet.Ports.Requests;
using Fleet.Ports.Harness;
using Fleet.Ports.Settings;
using Fleet.Platform.Releases;
using Fleet.Shared.Hooks;
using Fleet.Ui;

namespace Fleet.Cli.Composition;

public static class Adapters
{
    public static IFleetLog Log() => new FileLog();

    public static IProjectStore Projects() => new JsonProjectStore();

    public static IGitRunner Git() => new GitRunner();

    public static IReleaseClient Releases() => new HttpReleaseClient();

    public static IBinaryInstaller Installer() => new SelfInstall();

    private const string DefaultReleaseRepo = "Redmern/fleet-tui";

    public static string ReleaseRepo =>
        Environment.GetEnvironmentVariable("FLEET_REPO") is { Length: > 0 } repo
            ? repo
            : DefaultReleaseRepo;

    public static IKeymapStore Keymaps() => new JsonKeymapStore();

    public static ISettingsStore Settings() => new JsonSettingsStore();

    public static bool MainOrchestratorInNvim(string project) => Settings().Load(project).MainOrchestratorInNvim;

    public static bool SubOrchestratorsInNvim(string project) => Settings().Load(project).SubOrchestratorsInNvim;

    public static ISettingsSync SettingsSync() => new ClaudeSettingsSync();

    public static IHarnessConfig HarnessConfig() => new ClaudeHarnessConfig();

    public static IApprovalChannel Approvals() => new FileApprovalChannel();

    public static IApprovalInbox ApprovalInbox() => new FileApprovalChannel();

    public static IMcpServer McpServer(IFleetLog log)
    {
        var stdout = new StreamWriter(
            Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false,
            NewLine = "\n",
        };

        return new StdioMcpServer(Console.In, stdout, log);
    }

    public static (string Text, string? Cwd) ReadHookPayload()
    {
        var payload = HookIo.Read(Console.In);

        return (payload.Text, payload.Cwd);
    }

    public static HookEvent ReadHookEvent() =>
        HookIo.Event(HookIo.Read(Console.In), Environment.GetEnvironmentVariable(HookIo.ProjectDirVariable));

    public static string HookBlockJson(string note) => HookIo.Block(note);

    public static IAgentStateStore AgentStates() => new FileAgentStateStore();

    public static IActionRequestStore Requests() => new FileActionRequestStore();

    public static IWorkspaceRequestStore Workspaces() => new FileWorkspaceRequestStore();

    public static IAgentStore Agents() => new JsonAgentStore();

    public static Ports.Notifications.INoticeStore Notices() => new JsonNoticeStore();

    public static Ports.Sessions.ISessionStore Sessions() => new JsonSessionStore();

    public static Ports.Remotes.IRemoteMachines Remotes() =>
        new Platform.Remotes.NicknamedRemotes(
            new Platform.Mux.Embedded.EmbeddedRemotes(() => new Platform.Mux.Embedded.EmbeddedDriver(Platform.Mux.Embedded.Daemon.Endpoint.Default())),
            KnownRemotes());

    public static Ports.Remotes.IKnownRemoteStore KnownRemotes() => new JsonKnownRemoteStore();

    public static IDispatchHistory History() => new FileDispatchHistory();

    public static IIntentStore Intents() => new JsonIntentStore();

    public static ISlugNamer SlugNamer() => new ClaudeSlugNamer();

    public static INotifier Notifier() => new FileNotifyStore();

    public static MuxSelection Mux(IFleetLog log)
    {
        var chosen = DriverSelector.Choose(MuxEnvironment.Current(MuxEnvironment.OnPath));

        var embedded = chosen == DriverNames.Embedded;
        var unsupported = MuxTrouble.With(
            chosen, OnPath(DriverNames.WezTerm), embeddedReady: embedded && EmbeddedWiring.Ready);

        IMuxDriver inner = embedded ? EmbeddedWiring.Driver() : new WezTermDriver();

        return new MuxSelection(new FailSilentDriver(inner, log.Swallowed), chosen, unsupported);
    }

    public static string ConfigDirectory => FleetPaths.Config;

    public static string Executable => Environment.ProcessPath ?? "fleet";

    public static void MarkDashboardPane(string project)
    {
        WezTermUserVars.MarkDashboard(project);
        DashPaneMarker.Write(project, Environment.GetEnvironmentVariable("WEZTERM_PANE"));
    }

    public static string? DashPane(string project) => DashPaneMarker.Read(project);

    public static string NotifyFile => FileNotifyStore.File;

    public static string WorkspaceFile => FileWorkspaceRequestStore.File;

    public static void EmitUserVar(string name, string value) =>
        WezTermUserVars.Mark(name, value);

    public static bool OnPath(string exe) => MuxEnvironment.OnPath(exe);

    public static string? PickFolder(IMuxDriver mux, string project, string startIn)
    {
        var file = Path.Combine(
            Path.GetTempPath(), $"fleet-folder-{Guid.NewGuid():N}");

        var pane = SpawnHereAsync(mux, FileBrowser.Choose(startIn, project, CurrentWindow(mux), file))
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
        var pane = SpawnHereAsync(mux, FileBrowser.Browse(root, project, CurrentWindow(mux)))
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

    public static Task<PaneId> SpawnHereAsync(IMuxDriver mux, SpawnOptions options) =>
        mux.Caps.HasFlag(MuxCaps.Popup) && !mux.CurrentPane.IsNone
            ? mux.SpawnFloatingAsync(PaneId.None, options with { Workspace = null, SessionName = null, WindowId = null })
            : mux.SpawnAsync(options);

    public static string? CurrentWindow(IMuxDriver mux)
    {
        var panes = mux.ListPanesAsync().GetAwaiter().GetResult();

        if (!mux.CurrentPane.IsNone
            && panes.FirstOrDefault(p => p.Id == mux.CurrentPane) is { } mine)
        {
            return mine.WindowId;
        }

        return panes.FirstOrDefault(p => p.IsActive)?.WindowId;
    }

    public static ConfigWiring WireWezTermConfig()
    {
        var home = Home;

        var config = WezTermWiring.ConfigCandidates(home).FirstOrDefault(File.Exists);

        if (config is null)
        {
            var starter = WezTermWiring.DefaultConfig(home);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(starter)!);
                File.WriteAllText(starter, WezTermWiring.Starter());

                return new ConfigWiring(WiringState.Added, starter, "created a new config");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new ConfigWiring(WiringState.Failed, starter, e.Message);
            }
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

    public static string? SetupDrift(Keymap keymap)
    {
        var missing = SetupHandler.Harness.Where(t => !OnPath(t)).ToList();

        if (missing.Count > 0)
        {
            return $"setup: {string.Join(", ", missing)} not on PATH — run 'fleet setup'.";
        }

        if (DriverSelector.Choose(MuxEnvironment.Current(MuxEnvironment.OnPath)) != DriverNames.WezTerm)
        {
            return null;
        }

        var target = Path.Combine(WezTermWiring.ModuleDirectory(Home), WezTermWiring.Module);

        try
        {
            var wanted = WezTermKeybinds.Generate(
                keymap, Executable, FileWorkspaceRequestStore.File, FileNotifyStore.File);

            if (!File.Exists(target) || File.ReadAllText(target) != wanted)
            {
                return "setup: wezterm keybinds are outdated — run 'fleet setup' "
                    + "and reload wezterm.";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    public static string WriteKeybindModule(Keymap keymap)
    {
        var target = Path.Combine(WezTermWiring.ModuleDirectory(Home), WezTermWiring.Module);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(
            target,
            WezTermKeybinds.Generate(
                keymap, Executable, FileWorkspaceRequestStore.File, FileNotifyStore.File));

        File.WriteAllText(
            Path.Combine(WezTermWiring.ModuleDirectory(Home), WezTermTheme.Module),
            WezTermTheme.Generate());

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
