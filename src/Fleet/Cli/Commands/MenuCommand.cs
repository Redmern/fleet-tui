using System.Globalization;
using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Agents.CleanupAgents;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.MoveProject;
using Fleet.Features.Agents.OpenEditor;
using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Diagnostics.ViewLogs;
using Fleet.Features.Files.BrowseFiles;
using Fleet.Features.Menu.EditAidlc;
using Fleet.Features.Menu.EditFleetConfig;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Menu.EditSettings;
using Fleet.Features.Notifications.ShowNotices;
using Fleet.Features.Notifications.SyncNotices;
using Fleet.Features.Projects.CreateProject;
using Fleet.Features.Projects.LocateProject;
using Fleet.Features.Projects.LocateProject.Models;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.QuitProject;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Features.Projects.SwitchProject;
using Fleet.Features.Remotes.ManageRemotes;
using Fleet.Features.Sessions.SaveSession;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.ListRemotes;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Ports.Keymap;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Fleet.Ui.Enums;
using Fleet.Ui.Models;
using Terminal.Gui.App;

namespace Fleet.Cli.Commands;

public static class MenuCommand
{
    private const int LogTail = 400;

    private static readonly FleetAction[] MenuActions =
    [
        FleetAction.QuitFleet,
        FleetAction.FocusMain,
        FleetAction.SwitchProject,
        FleetAction.ListAgents,
        FleetAction.OpenEditor,
        FleetAction.BrowseFiles,
        FleetAction.Notifications,
        FleetAction.Remotes,
        FleetAction.SaveSession,
        FleetAction.OpenSettings,
    ];

    private static readonly FleetAction[] SettingsActions =
    [
        FleetAction.RebuildDashboard,
        FleetAction.EditSettings,
        FleetAction.EditFleetConfig,
        FleetAction.EditKeybinds,
        FleetAction.ViewLogs,
        FleetAction.CleanupProject,
        FleetAction.EditAidlcMode,
        FleetAction.EditAutoClose,
        FleetAction.EditClaudeProfile,
        FleetAction.EditMainOrchestratorInNvim,
        FleetAction.EditSubOrchestratorsInNvim,
    ];

    public static async Task<int> RunAsync(Invocation invocation)
    {
        var projects = Adapters.Projects();

        var project = invocation.Project is { } named
            ? projects.Load(named)
            : new ResolveProjectHandler(projects).ForDirectory(Environment.CurrentDirectory);

        var requested = invocation.Action is { } id
            ? FleetActionIds.Parse(id)
            : FleetAction.None;


        if (project is null && requested == FleetAction.SwitchProject
            && invocation.Project is { } shown && shown.StartsWith(RemoteMark, StringComparison.Ordinal))
        {
            using IApplication remoteApp = FleetUi.Start();
            await SwitchAcrossMachines(remoteApp, new Keymap(Adapters.Keymaps().Load()), projects.List(), null, shown[RemoteMark.Length..])
                .ConfigureAwait(false);
            return 0;
        }

        if (requested is FleetAction.OpenProject or FleetAction.NewProject || project is null)
        {
            return await PickProjectCommand.RunAsync(startNew: requested == FleetAction.NewProject).ConfigureAwait(false);
        }

        var keymaps = Adapters.Keymaps();
        var adder = new AddRepositoryHandler(Adapters.Git());

        using IApplication app = FleetUi.Start();

        var keymap = new Keymap(keymaps.Load());

        var chosen = requested;

        while (true)
        {
            if (chosen == FleetAction.None)
            {
                chosen = FleetUi.Menu(app, keymap, MenuActions);
            }

            if (chosen == FleetAction.OpenSettings)
            {
                chosen = FleetUi.Menu(app, keymap, SettingsActions);

                if (chosen == FleetAction.None && FleetModal.WentBack())
                {
                    continue;
                }
            }

            if (chosen == FleetAction.None)
            {
                return 0;
            }

            var code = await Perform(app, keymap, keymaps, adder, projects, project, chosen).ConfigureAwait(false);

            if (code != 0 || !FleetModal.WentBack())
            {
                return code;
            }

            keymap = new Keymap(keymaps.Load());
            chosen = Parent(chosen);
        }
    }

    public static FleetAction Parent(FleetAction action) =>
        SettingsActions.Contains(action) ? FleetAction.OpenSettings : FleetAction.None;

    private static async Task<int> Perform(
        IApplication app,
        Keymap keymap,
        IKeymapStore keymaps,
        AddRepositoryHandler adder,
        IProjectStore projects,
        Project project,
        FleetAction chosen)
    {
        switch (chosen)
        {
            case FleetAction.AddRepository:
                var repositories = await new ListRepositoriesHandler(Adapters.Git())
                    .HandleAsync(project.Root)
                    .ConfigureAwait(false);

                var known = await new ListRemotesHandler(Adapters.Git())
                    .HandleAsync([.. repositories.Select(r => r.Path)])
                    .ConfigureAwait(false);

                var request = AddRepositoryView.Show(app, project.Root, known, keymap);

                if (request is not null)
                {
                    var outcome = await adder.HandleAsync(request).ConfigureAwait(false);

                    if (!outcome.Succeeded)
                    {
                        FleetDialog.Error(app, "Could not add repository", outcome.Error!);
                    }
                }

                break;

            case FleetAction.QuitFleet:
                {
                    var quitMux = Adapters.Mux(Adapters.Log());

                    var inWindow = await ProjectsInWindow(quitMux.Driver, projects.List())
                        .ConfigureAwait(false);

                    if (inWindow.Count <= 1)
                    {
                        if (FleetDialog.Confirm(
                                app,
                                $"Quit fleet for {project.Name}?",
                                [],
                                "Quit"))
                        {
                            await Quit(project).ConfigureAwait(false);
                        }

                        break;
                    }

                    var quitChoice = FleetDialog.Choose(
                        app,
                        "Quit fleet?",
                        [$"This window has {inWindow.Count} projects open."],
                        "Quit all in this window",
                        $"Just {project.Name}");

                    if (quitChoice == DialogChoice.Cancelled)
                    {
                        break;
                    }

                    if (quitChoice == DialogChoice.Primary)
                    {
                        foreach (var toQuit in inWindow.OrderBy(p => SameProject(p, project)))
                        {
                            await Quit(toQuit).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await Quit(project).ConfigureAwait(false);
                    }

                    break;
                }

            case FleetAction.FocusMain:
                await FocusMain(project).ConfigureAwait(false);
                break;

            case FleetAction.SwitchProject when EmbeddedWiring.HandBack(FleetActionIds.For(FleetAction.SwitchProject)):
                break;

            case FleetAction.SwitchProject when SwitchProjectHandler.Applies(Adapters.Mux(Adapters.Log()).Driver):
                await SwitchAcrossMachines(app, keymap, projects.List(), project, onMachine: null).ConfigureAwait(false);
                break;

            case FleetAction.SwitchProject:
                {
                    var others = projects.List()
                        .Where(p => !string.Equals(
                            p.Name, project.Name, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (others.Count == 0)
                    {
                        FleetDialog.Error(
                            app, "Switch project", "No other projects are saved yet.");

                        break;
                    }

                    var switchLog = Adapters.Log();
                    var switchMux = Adapters.Mux(switchLog);
                    var located = await new LocateProjectHandler(switchMux.Driver)
                        .HandleAsync(projects.List())
                        .ConfigureAwait(false);

                    string Label(Project p) =>
                        Where(located, p).Open ? $"{p.Name}  (open)" : p.Name;

                    var labels = others.Select(Label).ToList();

                    var index = FleetPicker.Choose(app, "Switch project", labels, keymap);

                    if (index is not { } chosenIndex)
                    {
                        break;
                    }

                    var target = others[chosenIndex];
                    var clock = System.Diagnostics.Stopwatch.StartNew();

                    var switched = SwitchProjectHandler.Applies(switchMux.Driver)
                        ? await SwitchByWorkspace(switchMux.Driver, target, Where(located, target))
                            .ConfigureAwait(false)
                        : await SwitchByMoving(switchMux.Driver, projects.List(), target)
                            .ConfigureAwait(false);

                    switchLog.Write(
                        $"switch {project.Name} -> {target.Name}: {clock.ElapsedMilliseconds} ms ({switchMux.Driver.Name})");

                    if (switched is not null)
                    {
                        FleetDialog.Error(app, "Switch project", switched);
                    }

                    break;
                }

            case FleetAction.EditKeybinds:
                EditKeybindsView.Show(app, keymaps, keymap);
                break;

            case FleetAction.EditSettings:
                var settings = Adapters.Settings();

                EditSettingsView.Show(
                    app,
                    keymap,
                    project.Name,
                    settings.Load(project.Name),
                    next =>
                    {
                        settings.Save(project.Name, next);
                        return null;
                    });

                break;

            case FleetAction.EditAidlcMode:
                {
                    var aidlcSettings = Adapters.Settings();

                    EditAidlcView.Show(
                        app,
                        keymap,
                        project.Name,
                        aidlcSettings.Load(project.Name),
                        next =>
                        {
                            aidlcSettings.Save(project.Name, next);
                            return null;
                        });

                    break;
                }

            case FleetAction.EditMainOrchestratorInNvim:
                {
                    var hostSettings = Adapters.Settings();
                    var current = hostSettings.Load(project.Name);

                    var picked = FleetPicker.Choose(
                        app,
                        $"{SettingsDefaults.MainOrchestratorInNvimLabel} — {project.Name}",
                        [
                            new PickerEntry("on", "the main orchestrator runs claude inside nvim", "n"),
                            new PickerEntry("off", "the main orchestrator runs bare claude", "f"),
                        ],
                        keymap,
                        current.MainOrchestratorInNvim ? 0 : 1);

                    if (picked is not null)
                    {
                        hostSettings.Save(project.Name, current.WithMainOrchestratorInNvim(picked.Value == 0));
                    }

                    break;
                }

            case FleetAction.EditSubOrchestratorsInNvim:
                {
                    var hostSettings = Adapters.Settings();
                    var current = hostSettings.Load(project.Name);

                    var picked = FleetPicker.Choose(
                        app,
                        $"{SettingsDefaults.SubOrchestratorsInNvimLabel} — {project.Name}",
                        [
                            new PickerEntry("on", "dispatched sub-orchestrators run claude inside nvim", "n"),
                            new PickerEntry("off", "dispatched sub-orchestrators run bare claude", "f"),
                        ],
                        keymap,
                        current.SubOrchestratorsInNvim ? 0 : 1);

                    if (picked is not null)
                    {
                        hostSettings.Save(project.Name, current.WithSubOrchestratorsInNvim(picked.Value == 0));
                    }

                    break;
                }

            case FleetAction.EditAutoClose:
                {
                    var closeSettings = Adapters.Settings();
                    var current = closeSettings.Load(project.Name);
                    var title = $"{SettingsDefaults.AutoCloseLabel} — {project.Name}";

                    var picked = FleetPicker.Choose(
                        app,
                        title,
                        [
                            new PickerEntry("off", "agents stay open until you close them", "o"),
                        new PickerEntry("on", "close done or failed agents that sat idle", "n"),
                        ],
                        keymap,
                        current.AutoClose ? 1 : 0);

                    if (picked is null)
                    {
                        break;
                    }

                    if (picked.Value == 0)
                    {
                        closeSettings.Save(project.Name, current.WithAutoClose(false, current.AutoCloseMinutes));
                        break;
                    }

                    var answer = FleetDialog.Ask(
                        app,
                        title,
                        "Close after how many idle minutes?",
                        initial: current.AutoCloseMinutes.ToString(CultureInfo.InvariantCulture));

                    if (answer is null)
                    {
                        break;
                    }

                    if (!int.TryParse(answer.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes < 1)
                    {
                        FleetDialog.Error(app, title, $"'{answer}' is not a whole number of minutes.");
                        break;
                    }

                    closeSettings.Save(project.Name, current.WithAutoClose(true, minutes));
                    break;
                }

            case FleetAction.SaveSession:
                {
                    var window = EmbeddedWiring.CurrentWindow().Select(e => new WindowEntry(e.Name, e.Host, e.Shown)).ToList();
                    if (window.Count == 0)
                    {
                        FleetDialog.Error(app, "Save window as session", "Sessions need the built-in multiplexer; fleet sees no projects in this window.");
                        break;
                    }

                    var name = FleetDialog.Ask(
                        app,
                        "Save window as session",
                        $"Name for {SaveSessionHandler.Describe(SaveSessionHandler.For("window", window))}",
                        initial: project.Name);

                    if (name is null)
                    {
                        break;
                    }

                    var saved = new SaveSessionHandler(Adapters.Sessions()).Handle(name, window);
                    if (!saved.Succeeded)
                    {
                        FleetDialog.Error(app, "Save window as session", saved.Error!);
                    }

                    break;
                }

            case FleetAction.Remotes:
                ManageRemotesView.Show(app, keymap, Adapters.Remotes(), Adapters.KnownRemotes());
                break;

            case FleetAction.Notifications:
                {
                    var noticeMux = Adapters.Mux(Adapters.Log()).Driver;
                    var notices = Adapters.Notices();
                    var inWindow = SwitchProjectHandler.Applies(noticeMux)
                        ? (await ProjectsInWindow(noticeMux, projects.List()).ConfigureAwait(false)).Select(p => p.Name).Append(project.Name).ToList()
                        : null;
                    var remoteNotices = inWindow is null ? null : EmbeddedWiring.WindowRemoteNotices();
                    ShowNoticesView.Show(
                        app,
                        keymap,
                        notices,
                        (project, keys) =>
                        {
                            if (remoteNotices?.Dismiss(project, keys) != true)
                            {
                                notices.Save(project, NoticeSync.Dismiss(notices.Load(project), keys, DateTime.UtcNow));
                            }
                        },
                        notice => remoteNotices?.Locate(notice) is var (host, remoteProject)
                            ? ShowRemoteNotice(host, remoteProject)
                            : OpenNotice(noticeMux, projects.List(), notice),
                        inWindow,
                        remoteNotices?.Source);
                    break;
                }

            case FleetAction.EditClaudeProfile:
                {
                    if (EmbeddedWiring.ClaudeProfiles(project.Root) is not var (byFolder, profiles))
                    {
                        FleetDialog.Error(
                            app, "Claude profile", $"No profiles found in {EmbeddedWiring.ProfilesFile}.");

                        break;
                    }

                    var picked = FleetPicker.Choose(
                        app,
                        $"Claude profile — {project.Name} (for panes opened from now on)",
                        [
                            new PickerEntry("by folder", $"from ~/.profiles.psd1: {byFolder}", "f"),
                            .. profiles.Select((p, i) => new PickerEntry(
                                p.Name, p.Folder, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))),
                        ],
                        keymap,
                        project.ClaudeProfile is { } current
                            ? Math.Max(0, profiles.ToList().FindIndex(p => string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase)) + 1)
                            : 0);

                    if (picked is { } index)
                    {
                        projects.Save(project with { ClaudeProfile = index == 0 ? null : profiles[index - 1].Name });
                    }

                    break;
                }

            case FleetAction.EditFleetConfig:
                {
                    EditFleetConfigHandler.Ensure(project.Root);
                    var configMux = Adapters.Mux(Adapters.Log());

                    if (configMux.Unsupported is not null)
                    {
                        FleetDialog.Error(app, "Edit fleet config", configMux.Unsupported);
                        break;
                    }

                    var configPane = await Adapters.SpawnHereAsync(
                        configMux.Driver,
                        new SpawnOptions
                        {
                            Cwd = project.Root,
                            SessionName = project.Name,
                            WindowId = Adapters.CurrentWindow(configMux.Driver),
                            Args = AgentHarness.BrowseCommandFor("fleet config"),
                        }).ConfigureAwait(false);

                    if (configPane.IsNone)
                    {
                        FleetDialog.Error(
                            app, "Edit fleet config",
                            $"the {configMux.Driver.Name} multiplexer did not respond. Run 'fleet doctor'.");

                        break;
                    }

                    await configMux.Driver.SetTitleAsync(configPane, "fleet config").ConfigureAwait(false);
                    await configMux.Driver.FocusPaneAsync(configPane).ConfigureAwait(false);

                    break;
                }

            case FleetAction.OpenEditor:
                {
                    var editorMux = Adapters.Mux(Adapters.Log());

                    if (editorMux.Unsupported is not null)
                    {
                        FleetDialog.Error(app, "Open editor here", editorMux.Unsupported);
                        break;
                    }

                    var driver = editorMux.Driver;
                    var agent = OpenEditorHandler.Caller(
                        await driver.ListPanesAsync().ConfigureAwait(false),
                        driver.CurrentPane,
                        driver.Caps.HasFlag(MuxCaps.Popup),
                        Environment.CurrentDirectory,
                        new ListAgentsHandler(Adapters.Agents()).Handle(project.Name));

                    if (agent is null)
                    {
                        FleetDialog.Error(app, "Open editor here", "This pane is not an agent or sub-orchestrator.");
                        break;
                    }

                    var opened = await new OpenEditorHandler(driver)
                        .HandleAsync(project.Name, agent, project.Root)
                        .ConfigureAwait(false);

                    if (!opened.Succeeded)
                    {
                        FleetDialog.Error(app, "Open editor here", opened.Error!);
                    }

                    break;
                }

            case FleetAction.BrowseFiles:
                if (!Adapters.OnPath(FileBrowser.Command))
                {
                    Console.Error.WriteLine(
                        $"fleet: {FileBrowser.Command} is not on PATH. Install it to browse files.");

                    return 1;
                }

                Adapters.BrowseFolder(
                    Adapters.Mux(Adapters.Log()).Driver, project.Name, project.Root);

                break;

            case FleetAction.RebuildDashboard:
                Adapters.Requests().Submit(project.Name, FleetAction.RebuildDashboard);
                break;

            case FleetAction.CleanupProject:
                {
                    var summary = new CleanupHandler(Adapters.Agents()).Handle(project.Name);

                    Adapters.Log().Write(LogTag.For(project.Name, summary));
                    FleetDialog.Error(app, "Clean up", summary);
                    break;
                }

            case FleetAction.ViewLogs:
                var log = Adapters.Log();

                ViewLogsView.Show(
                    app,
                    keymap,
                    project.Name,
                    LogParser.For(project.Name, LogParser.Parse(log.Tail(LogTail))));

                break;

            case FleetAction.ListAgents:
                {
                    var dashGit = Adapters.Git();
                    var dashMux = Adapters.Mux(Adapters.Log());
                    var dashLog = Adapters.Log();
                    var dashApprovals = Adapters.ApprovalInbox();

                    ShowDashboardView.Show(
                        app,
                        project.Name,
                        keymap,
                        DashboardWiring.For(
                            app,
                            project,
                            keymap,
                            keymaps,
                            dashGit,
                            dashMux.Driver,
                            Adapters.Agents(),
                            Adapters.Requests(),
                            Adapters.Workspaces(),
                            Adapters.Settings(),
                            Adapters.SettingsSync(),
                            dashApprovals,
                            dashLog),
                        menu: true);

                    break;
                }
        }

        return 0;
    }


    private static async Task<string?> ShowRemoteNotice(string host, string project)
    {
        try
        {
            await Adapters.Remotes().ShowHereAsync(host, project).ConfigureAwait(false);
            return null;
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException e)
        {
            return e.Message;
        }
    }

    private static async Task<string?> OpenNotice(IMuxDriver mux, IReadOnlyList<Project> all, Ports.Notifications.Models.Notice notice)
    {
        if (all.FirstOrDefault(p => string.Equals(p.Name, notice.Project, StringComparison.OrdinalIgnoreCase)) is not { } owner)
        {
            return $"{notice.Project} is no longer a project";
        }

        var agent = new ListAgentsHandler(Adapters.Agents()).Handle(owner.Name)
            .FirstOrDefault(a => PathKey.Same(a.Worktree, notice.Worktree));

        if (agent is null)
        {
            return $"{notice.Agent} is gone";
        }

        if (SwitchProjectHandler.Applies(mux))
        {
            var located = await new LocateProjectHandler(mux).HandleAsync([owner]).ConfigureAwait(false);
            if (Where(located, owner).Open)
            {
                await new SwitchProjectHandler(mux).HandleAsync(owner.Name).ConfigureAwait(false);
            }
        }

        var opened = await new Features.Agents.OpenAgent.OpenAgentHandler(mux, Adapters.Agents())
            .HandleAsync(owner.Name, agent, owner.Root)
            .ConfigureAwait(false);

        return opened.Succeeded ? null : opened.Error;
    }

    private const string RemoteMark = "@";

    private const string SwitchTitle = "Switch project  (a-z here, A-Z new window)";

    private static async Task SwitchAcrossMachines(
        IApplication app, Keymap keymap, IReadOnlyList<Project> saved, Project? current, string? onMachine)
    {
        var switchLog = Adapters.Log();
        var switchMux = Adapters.Mux(switchLog);
        var all = saved.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var located = await new LocateProjectHandler(switchMux.Driver)
            .HandleAsync(all)
            .ConfigureAwait(false);

        var localEntries = all.Select(p => new PickerEntry(p.Name, Where(located, p) switch
        {
            { InWindow: true } => "this window",
            { InOtherWindow: true } => "another window",
            { Open: true } => "open",
            _ => string.Empty,
        })).ToList();
        var here = current is null ? 0 : Math.Max(0, all.FindIndex(p => SameProject(p, current)));
        var from = current?.Name ?? RemoteMark + onMachine;

        var remotes = Adapters.Remotes();
        var machines = (await remotes.ListAsync().ConfigureAwait(false))
            .Where(m => m.State == Ports.Remotes.Enums.RemoteState.Connected)
            .ToList();

        var tabs = SwitchTabs.For(
            localEntries, [.. all.Where(p => Where(located, p).InWindow).Select(p => p.Name)], machines, keymap.TextFor(FleetAction.NewProject));
        var machineTab = machines.FindIndex(m => string.Equals(m.Name, onMachine, StringComparison.OrdinalIgnoreCase));
        var (startTab, startEntry) = machineTab >= 0
            ? tabs.Start(t => t.Host == machines[machineTab].Host, (tabs.MachineTab(machineTab), 0))
            : tabs.Start(t => t.Host is null && string.Equals(t.Project, current?.Name, StringComparison.OrdinalIgnoreCase), (tabs.ThisMachine, here));
        var tabbed = FleetTabbedPicker.Choose(app, SwitchTitle, tabs.Tabs, keymap, startTab, startEntry);

        if (tabbed is var (newTab, newIndex, newInWindow) && tabs.Targets[newTab][newIndex] is { IsNew: true } fresh)
        {
            await NewProjectFrom(app, switchMux.Driver, remotes, fresh.Host, newInWindow).ConfigureAwait(false);
            switchLog.Write($"switch {from} -> new project{(fresh.Host is null ? string.Empty : " on " + fresh.Host)}");
            return;
        }

        if (tabbed is var (tabIndex, entryIndex, remoteWindow)
            && tabs.Targets[tabIndex][entryIndex] is { Host: { } host } remoteTarget)
        {
            try
            {
                if (remoteWindow)
                {
                    await remotes.OpenInNewWindowAsync(host, remoteTarget.Project).ConfigureAwait(false);
                }
                else
                {
                    await remotes.ShowHereAsync(host, remoteTarget.Project).ConfigureAwait(false);
                }
            }
            catch (Ports.Mux.Exceptions.MuxUnavailableException e)
            {
                FleetDialog.Error(app, "Switch project", e.Message);
            }

            switchLog.Write($"switch {from} -> {remoteTarget.Project} on {host}{(remoteWindow ? " (new window)" : string.Empty)}");
            return;
        }

        (int Index, bool NewWindow)? picked = tabbed is var (localTab, localIndex, localWindow)
            ? (all.FindIndex(p => string.Equals(p.Name, tabs.Targets[localTab][localIndex].Project, StringComparison.OrdinalIgnoreCase)), localWindow)
            : null;

        if (picked is not var (chosenIndex, newWindow) || chosenIndex < 0)
        {
            return;
        }

        var target = all[chosenIndex];
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var switched = newWindow
            ? await OpenInNewWindow(switchMux.Driver, target, Where(located, target)).ConfigureAwait(false)
            : await SwitchByWorkspace(switchMux.Driver, target, Where(located, target)).ConfigureAwait(false);

        switchLog.Write(
            $"switch {from} -> {target.Name}{(newWindow ? " (new window)" : string.Empty)}: {clock.ElapsedMilliseconds} ms");

        if (switched is not null)
        {
            FleetDialog.Error(app, "Switch project", switched);
        }
    }

    private static async Task NewProjectFrom(
        IApplication app, IMuxDriver mux, Ports.Remotes.IRemoteMachines remotes, string? host, bool newWindow)
    {
        if (host is not null)
        {
            try
            {
                await remotes.NewProjectAsync(host).ConfigureAwait(false);
            }
            catch (Ports.Mux.Exceptions.MuxUnavailableException e)
            {
                FleetDialog.Error(app, "New project", e.Message);
            }

            return;
        }

        if (CreateProjectView.Show(app, new CreateProjectHandler(Adapters.Projects()), PickProjectCommand.FolderPicker(mux)) is not { } made)
        {
            return;
        }

        var opened = newWindow
            ? await OpenInNewWindow(mux, made, ProjectLocation.Closed).ConfigureAwait(false)
            : await SwitchByWorkspace(mux, made, ProjectLocation.Closed).ConfigureAwait(false);

        if (opened is not null)
        {
            FleetDialog.Error(app, "New project", opened);
        }
    }

    private static bool SameProject(Project a, Project b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    private static ProjectLocation Where(
        IReadOnlyDictionary<string, ProjectLocation> located, Project project) =>
        located.TryGetValue(project.Name, out var at) ? at : ProjectLocation.Closed;

    private static async Task<List<Project>> ProjectsInWindow(
        IMuxDriver mux, IReadOnlyList<Project> allProjects)
    {
        var located = await new LocateProjectHandler(mux).HandleAsync(allProjects)
            .ConfigureAwait(false);

        return allProjects.Where(p => Where(located, p).InWindow).ToList();
    }

    private static async Task<string?> SwitchByWorkspace(
        IMuxDriver mux, Project target, ProjectLocation where)
    {
        if (!where.Open)
        {
            await OpenProjectFlow(mux, target).ConfigureAwait(false);
        }

        var shown = await new SwitchProjectHandler(mux).HandleAsync(target.Name)
            .ConfigureAwait(false);

        return shown.Succeeded ? null : shown.Error;
    }

    private static async Task<string?> OpenInNewWindow(IMuxDriver mux, Project target, ProjectLocation where)
    {
        if (!where.Open)
        {
            await OpenProjectFlow(mux, target).ConfigureAwait(false);
        }

        try
        {
            await mux.OpenWindowAsync(target.Name).ConfigureAwait(false);
            return null;
        }
        catch (NotSupportedException e)
        {
            return e.Message;
        }
    }

    private static async Task<string?> SwitchByMoving(
        IMuxDriver mux, IReadOnlyList<Project> allProjects, Project target)
    {
        var panes = await mux.ListPanesAsync().ConfigureAwait(false);
        var self = mux.CurrentPane.IsNone ? null : mux.CurrentPane.Value;
        var currentWindow = panes.FirstOrDefault(p => p.Id == mux.CurrentPane)?.WindowId;

        var toPark = (await ProjectsInWindow(mux, allProjects).ConfigureAwait(false))
            .Where(p => !string.Equals(p.Name, target.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var mover = new MoveProjectHandler(mux, Adapters.MainOrchestratorInNvim(target.Name));

        foreach (var park in toPark)
        {
            await mover
                .ParkAsync(
                    park.Name,
                    park.Root,
                    new ListAgentsHandler(Adapters.Agents()).Handle(park.Name),
                    Adapters.DashPane(park.Name),
                    self)
                .ConfigureAwait(false);
        }

        var dash = panes.FirstOrDefault(x => PathKey.Same(x.Cwd, target.Root));

        if (dash is not null && dash.WindowId == currentWindow)
        {
            await mux.FocusPaneAsync(dash.Id).ConfigureAwait(false);
            return null;
        }

        if (dash is null)
        {
            await OpenProjectFlow(mux, target, currentWindow).ConfigureAwait(false);
            return null;
        }

        var moved = await mover
            .HandleAsync(
                target.Name,
                target.Root,
                new ListAgentsHandler(Adapters.Agents()).Handle(target.Name),
                currentWindow,
                Adapters.DashPane(target.Name),
                self,
                Adapters.Executable)
            .ConfigureAwait(false);

        return moved.Succeeded ? null : moved.Error;
    }

    private static async Task Quit(Project project)
    {
        var agents = new ListAgentsHandler(Adapters.Agents()).Handle(project.Name);
        var mux = Adapters.Mux(Adapters.Log());

        await new QuitProjectHandler(mux.Driver, Adapters.Agents())
            .HandleAsync(project.Name, project.Root, agents)
            .ConfigureAwait(false);
    }

    private static async Task OpenProjectFlow(
        IMuxDriver mux, Project project, string? windowId = null)
    {
        var result = await new OpenProjectHandler(mux)
            .HandleAsync(new OpenProjectCommand(
                project, AgentHarness.Orchestrator, Adapters.Executable, windowId, Adapters.MainOrchestratorInNvim(project.Name)))
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return;
        }

        var agents = new ListAgentsHandler(Adapters.Agents()).Handle(project.Name);

        var runnable = agents
            .Where(a => Adapters.OnPath(
                AgentHarness.CommandFor(a.Harness, orchestratorInNvim: Adapters.SubOrchestratorsInNvim(project.Name))[0]))
            .ToList();

        await new RestoreSessionHandler(mux, Adapters.SubOrchestratorsInNvim(project.Name), Adapters.Agents())
            .HandleAsync(project.Name, project.Root, runnable)
            .ConfigureAwait(false);

        await mux.FocusPaneAsync(result.Value.DashPane).ConfigureAwait(false);
    }

    private static async Task FocusMain(Project project)
    {
        var mux = Adapters.Mux(Adapters.Log()).Driver;

        if (SwitchProjectHandler.Applies(mux))
        {
            var located = await new LocateProjectHandler(mux).HandleAsync([project])
                .ConfigureAwait(false);

            if (Where(located, project) is { Open: true, ShownHere: false })
            {
                await new SwitchProjectHandler(mux).HandleAsync(project.Name).ConfigureAwait(false);
            }
        }

        var panes = await mux.ListPanesAsync().ConfigureAwait(false);

        var dashboard = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, project.Root)
            && !FleetWorkspaces.IsHidden(p.SessionName));

        if (dashboard is not null)
        {
            await mux.FocusPaneAsync(dashboard.Id).ConfigureAwait(false);
        }
    }
}
