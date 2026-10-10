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
using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Setup.RunSetup;
using Fleet.Features.Setup.RunSetup.Models;
using Fleet.Features.Themes.ManageThemes;
using Fleet.Platform.Nvim;
using Fleet.Platform.Harness;
using Fleet.Platform.Hooks;
using Fleet.Platform.Storage;
using Fleet.Platform.Themes;
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
using Fleet.Shared;
using Fleet.Shared.Hooks;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Themes;
using Fleet.Ui;

namespace Fleet.Cli.Composition;

public static class Adapters
{
    public static IFleetLog Log() => new FileLog();

    public static IProjectStore Projects() => new JsonProjectStore();

    public static IGitRunner Git() => new GitRunner();

    public static IReleaseClient Releases() => new HttpReleaseClient();

    public static IBinaryInstaller Installer() => new SelfInstall();

    public static IUpdateCheckCache UpdateChecks() => new JsonUpdateCheckCache();

    private const string DefaultReleaseRepo = "Redmern/fleet-tui";

    public static string ReleaseRepo =>
        Environment.GetEnvironmentVariable("FLEET_REPO") is { Length: > 0 } repo
            ? repo
            : DefaultReleaseRepo;

    public static IKeymapStore Keymaps() => new ApplyingKeymapStore(new JsonKeymapStore(), () => _ = Task.Run(KeybindWiring.ApplyQuietly));

    public static ISettingsStore Settings() => new JsonSettingsStore();

    public static IIsoMode Iso() => new JsonIsoMode();

    public static bool MainOrchestratorInNvim(string project) => Settings().Load(project).MainOrchestratorInNvim;

    public static bool SubOrchestratorsInNvim(string project) => Settings().Load(project).SubOrchestratorsInNvim;

    public static RoleModels Models(string project) => Settings().Load(project).Models;

    public static bool SubagentGuidance(string project) => Settings().Load(project).SubagentGuidance;

    public static RoleModel HeadModel() => new JsonSettingsStore().LoadHead();

    public static void SaveHeadModel(RoleModel model) => new JsonSettingsStore().SaveHead(model);

    public static bool ShowMenuKeys() => new JsonSettingsStore().LoadShowMenuKeys();

    public static void SaveShowMenuKeys(bool shown) => new JsonSettingsStore().SaveShowMenuKeys(shown);

    public static NvimConfig LoadNvimConfig() => new JsonSettingsStore().LoadNvim();

    public static void SaveNvimConfig(NvimConfig nvim) => new JsonSettingsStore().SaveNvim(nvim);
    public static ButtonHints ButtonHints() => new JsonSettingsStore().LoadButtonHints();

    public static void SaveButtonHints(ButtonHints hints) => new JsonSettingsStore().SaveButtonHints(hints);

    public static ToggleSettingHandler Toggles() =>
        new(
            Settings(),
            shared =>
            {
                SaveShowMenuKeys(shared.ShowMenuKeys);
                SaveNvimConfig(shared.Nvim);
                SaveButtonHints(shared.ButtonHints);
            },
            (title, body) => Platform.Notifications.DesktopToast.Show(title, body));

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
        HookIo.Event(
            HookIo.Read(Console.In),
            Environment.GetEnvironmentVariable(HookIo.ProjectDirVariable),
            Environment.GetEnvironmentVariable(HookIo.MessagingSocketVariable));

    public static string HookBlockJson(string note) => HookIo.Block(note);

    public static IAgentStateStore AgentStates() => new FileAgentStateStore();

    public static IAgentInboxes AgentInboxes() => new StatusFileInboxes(AgentStates());

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

    public static Ports.Forwards.IPortForwards Forwards() =>
        new Platform.Mux.Embedded.EmbeddedForwards(() => new Platform.Mux.Embedded.EmbeddedDriver(Platform.Mux.Embedded.Daemon.Endpoint.Default()));

    public static Ports.Browser.IBrowserLauncher Browser() => new Platform.Forwards.SystemBrowser();

    public static IDispatchHistory History() => new FileDispatchHistory();

    public static IIntentStore Intents() => new JsonIntentStore();

    public static ISlugNamer SlugNamer() => new ClaudeSlugNamer();

    public static INotifier Notifier() => new FileNotifyStore();

    public static MuxSelection Mux(IFleetLog log)
    {
        var chosen = DriverSelector.Choose(MuxEnvironment.Current(MuxEnvironment.OnPath));

        var embedded = chosen == DriverNames.Embedded;
        var unsupported = MuxTrouble.With(chosen, embeddedReady: embedded && EmbeddedWiring.Ready);

        IMuxDriver inner = EmbeddedWiring.Driver();

        return new MuxSelection(
            new FailSilentDriver(new NvimConfigDriver(inner, UseFleetNvimConfig), log.Swallowed), chosen, unsupported);
    }

    public static NvimSetup InspectNvim(bool install)
    {
        var fleetConfig = LoadNvimConfig() == NvimConfig.Fleet;
        var version = NvimVersion.Parse(FleetNvimConfig.NvimVersionOutput());
        var written = fleetConfig && (install ? FleetNvimConfig.Install() && WriteNvimPalette() : Directory.Exists(FleetNvimConfig.Directory));

        return new NvimSetup(
            fleetConfig,
            written && (!install || version is null || !NvimVersion.SupportsAppName(version) || FleetNvimConfig.InstallPlugins()),
            FleetNvimConfig.Directory,
            version);
    }

    private static bool UseFleetNvimConfig() =>
        LoadNvimConfig() == NvimConfig.Fleet && FleetNvimConfig.EnsureInstalled() && WriteNvimPalette();

    private static bool WriteNvimPalette()
    {
        try
        {
            new NvimThemeTarget(FleetNvimConfig.Directory).Apply(Themes().Active());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log().Swallowed(e);
        }

        return true;
    }

    public static string ConfigDirectory => FleetPaths.Config;

    public static string Executable => Environment.ProcessPath ?? "fleet";

    public static string? DashPane(string project) => DashPaneMarker.Read(project);

    public static string NotifyFile => FileNotifyStore.File;

    public static string WorkspaceFile => FileWorkspaceRequestStore.File;

    public static bool OnPath(string exe) => MuxEnvironment.OnPath(exe);

    public static string? PickFolder(IMuxDriver mux, string project, string startIn) =>
        WithFolderFile(file =>
        {
            var pane = SpawnHereAsync(mux, FileBrowser.Choose(startIn, project, CurrentWindow(mux), file))
                .GetAwaiter()
                .GetResult();

            if (pane.IsNone)
            {
                return null;
            }

            mux.SetTitleAsync(pane, FileBrowser.ChooseTitle).GetAwaiter().GetResult();
            mux.FocusPaneAsync(pane).GetAwaiter().GetResult();

            return WaitForChoice(mux, pane, file);
        });

    public static string? PickFolderInTerminal(string startIn) =>
        WithFolderFile(file =>
        {
            var options = FileBrowser.Choose(startIn, string.Empty, null, file);
            var start = new System.Diagnostics.ProcessStartInfo(options.Args[0])
            {
                UseShellExecute = false,
                WorkingDirectory = startIn,
            };

            foreach (var arg in options.Args.Skip(1))
            {
                start.ArgumentList.Add(arg);
            }

            try
            {
                using var process = System.Diagnostics.Process.Start(start);
                process?.WaitForExit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }

            return File.Exists(file) ? FileBrowser.Chosen(file, ReadOrNull) : null;
        });

    private static string? WithFolderFile(Func<string, string?> pick)
    {
        var file = Path.Combine(
            Path.GetTempPath(), $"fleet-folder-{Guid.NewGuid():N}");

        try
        {
            return pick(file);
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

    public static bool CanShowPaneHere(IMuxDriver mux) =>
        !mux.Caps.HasFlag(MuxCaps.Workspaces)
        || (!mux.CurrentPane.IsNone
            && mux.ListPanesAsync().GetAwaiter().GetResult().Any(p => p.Id == mux.CurrentPane));

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

        return null;
    }

    public static ManageThemesHandler Themes() =>
        new(new FileThemeStore(FleetPaths.Config), new OmarchyThemeSource(Home));
}
