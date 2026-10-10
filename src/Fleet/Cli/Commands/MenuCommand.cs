using System.Globalization;
using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Agents.CleanupAgents;
using Fleet.Features.Agents.HideAgent;
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
using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowReleaseNotes;
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
using Fleet.Features.Updates.ShowVersion;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.ListRemotes;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Ports.Keymap;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Themes.Enums;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui;
using Fleet.Ui.Enums;
using Fleet.Ui.Models;
using Terminal.Gui.App;

namespace Fleet.Cli.Commands;

public static class MenuCommand
{
    private const int LogTail = 400;

    public static async Task<int> RunAsync(Invocation invocation)
    {
        _ = FreshBuild.ThisProcess;
        var projects = Adapters.Projects();

        var project = invocation.Project is { } named
            ? projects.Load(named)
            : new ResolveProjectHandler(projects).ForDirectory(Environment.CurrentDirectory);

        var requested = invocation.Action is { } id
            ? FleetActionIds.Parse(id)
            : FleetAction.None;


        if (project is null && requested is FleetAction.SwitchProject or FleetAction.Notifications
            && invocation.Project is { } shown && shown.StartsWith(RemoteMark, StringComparison.Ordinal))
        {
            using IApplication remoteApp = FleetUi.Start();
            var remoteKeymap = new Keymap(Adapters.Keymaps().Load());
            if (requested == FleetAction.Notifications)
            {
                await ShowNotices(remoteApp, remoteKeymap, projects, null).ConfigureAwait(false);
            }
            else
            {
                await SwitchAcrossMachines(remoteApp, remoteKeymap, projects.List(), null, shown[RemoteMark.Length..])
                    .ConfigureAwait(false);
            }

            return 0;
        }

        if (requested is FleetAction.OpenProject or FleetAction.NewProject || project is null)
        {
            return await PickProjectCommand.RunAsync(startNew: requested == FleetAction.NewProject).ConfigureAwait(false);
        }

        if (RunsHeadless(requested))
        {
            Adapters.Toggles().Toggle(project.Name, requested);
            return 0;
        }

        var keymaps = Adapters.Keymaps();
        var adder = new AddRepositoryHandler(Adapters.Git());

        FleetAction pick;

        using (IApplication app = FleetUi.Start())
        {
            var keymap = new Keymap(keymaps.Load());

            var chosen = requested;
            var switchedTo = FleetAction.None;
            using var opening = new CancellationTokenSource();
            var switching = SwitchWhenOpenedAsync(
                app,
                invocation.Action is null ? WarmMenuWiring.WaitForOpenAsync(opening.Token) : Task.FromResult<string?>(null),
                action => switchedTo = action);

            try
            {
                while (true)
                {
                    if (chosen == FleetAction.None || FleetMenus.IsSubmenu(chosen))
                    {
                        var submenu = chosen;

                        chosen = await ShowMenu(app, keymap, project, submenu).ConfigureAwait(false);

                        if (chosen == FleetAction.None && submenu != FleetAction.None && FleetModal.WentBack())
                        {
                            chosen = FleetMenus.Parent(submenu);
                            continue;
                        }

                        if (chosen == FleetAction.None && submenu == FleetAction.None && switchedTo != FleetAction.None)
                        {
                            (chosen, switchedTo) = (switchedTo, FleetAction.None);
                            if (chosen is FleetAction.OpenProject or FleetAction.NewProject)
                            {
                                pick = chosen;
                                break;
                            }

                            continue;
                        }

                        if (chosen == FleetAction.None)
                        {
                            return 0;
                        }

                        continue;
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
            finally
            {
                await opening.CancelAsync().ConfigureAwait(false);
                await switching.ConfigureAwait(false);
            }
        }

        return await PickProjectCommand.RunAsync(startNew: pick == FleetAction.NewProject).ConfigureAwait(false);
    }

    private static async Task SwitchWhenOpenedAsync(IApplication app, Task<string?> opened, Action<FleetAction> switchTo)
    {
        if (await opened.ConfigureAwait(false) is { } id && FleetActionIds.Parse(id) is var action and not FleetAction.None)
        {
            app.Invoke(() =>
            {
                switchTo(action);
                app.RequestStop();
            });
        }
    }

    public static FleetAction Parent(FleetAction action) => FleetMenus.Parent(action);

    public static bool RunsHeadless(FleetAction action) => FleetMenus.IsToggle(action);

    public static Pane? DashboardPane(IReadOnlyList<Pane> panes, string root, string? dashPane)
    {
        var shown = panes
            .Where(p => PathKey.Same(p.Cwd, root) && !FleetWorkspaces.IsHidden(p.SessionName))
            .ToList();

        return shown.FirstOrDefault(p => p.Id.Value == dashPane) ?? shown.FirstOrDefault();
    }

    private static async Task<FleetAction> ShowMenu(
        IApplication app, Keymap keymap, Project project, FleetAction submenu)
    {
        var sections = FleetMenus.For(submenu);

        if (submenu == FleetAction.None && !await CanOpenEditor(project).ConfigureAwait(false))
        {
            sections = FleetMenus.Without(sections, FleetAction.OpenEditor);
        }

        var settings = Adapters.Settings();
        var current = settings.Load(project.Name);
        var head = Adapters.HeadModel();
        var toggles = Adapters.Toggles();

        return FleetUi.Menu(
            app,
            keymap,
            submenu,
            sections,
            action => FleetMenus.Value(action, current, head),
            action =>
            {
                current = toggles.Flip(project.Name, action, current);
                return FleetMenus.Value(action, current, head);
            },
            () => current.ShowMenuKeys,
            () => current.ButtonHints);
    }

    private static async Task<bool> CanOpenEditor(Project project)
    {
        var mux = Adapters.Mux(Adapters.Log());

        return await OpenEditorHandler.CanOpenEditorAsync(
                mux.Unsupported is null ? mux.Driver : null,
                Environment.CurrentDirectory,
                new ListAgentsHandler(Adapters.Agents()).Handle(project.Name))
            .ConfigureAwait(false);
    }

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

            case FleetAction.EditTheme:
                {
                    var themes = Adapters.Themes();
                    var all = themes.List();
                    var active = themes.Active().Name;

                    var pickedTheme = FleetPicker.Choose(
                        app,
                        "Theme — fleet, nvim, claude and yazi",
                        [.. all.Select(t => new PickerEntry(
                            t.Title,
                            t.Name == active ? $"{t.Name}  (active)" : t.Name))],
                        keymap,
                        Math.Max(0, all.ToList().FindIndex(t => t.Name == active)));

                    if (pickedTheme is not { } themeIndex)
                    {
                        break;
                    }

                    var set = themes.Set(all[themeIndex].Name);

                    if (!set.Succeeded)
                    {
                        FleetDialog.Error(app, "Theme", set.Error!);
                        break;
                    }

                    var problems = ThemeWiring.Apply(set.Value, project.Root)
                        .Where(a => a.Outcome == ThemeOutcome.Failed)
                        .Select(a => a.Line)
                        .ToList();

                    if (problems.Count > 0)
                    {
                        FleetDialog.Error(app, "Theme", string.Join("\n", problems));
                    }

                    break;
                }

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

            case FleetAction.EditHeadModel:
            case FleetAction.EditMainModel:
            case FleetAction.EditSubModel:
            case FleetAction.EditAgentModel:
                {
                    var modelSettings = Adapters.Settings();
                    var current = modelSettings.Load(project.Name);
                    var global = chosen == FleetAction.EditHeadModel;
                    var title = global
                        ? $"{ModelRows.Title(chosen)} — all projects"
                        : $"{ModelRows.Title(chosen)} — {project.Name}";
                    var was = ModelRows.Current(chosen, current, Adapters.HeadModel());

                    var pickedModel = FleetPicker.Choose(app, title, ModelRows.ModelEntries, keymap, ModelRows.ModelIndex(was));

                    if (pickedModel is null)
                    {
                        break;
                    }

                    var model = ModelRows.ModelAt(pickedModel.Value);

                    if (model is null)
                    {
                        var typed = FleetDialog.Ask(
                            app, title, "Model alias or full model ID (inherit for the profile default):", initial: was.Model);

                        if (typed is null)
                        {
                            break;
                        }

                        model = ModelRows.Typed(typed);

                        if (model is null)
                        {
                            FleetDialog.Error(app, title, $"'{typed}' is not a model alias or ID fleet can pass to claude.");
                            break;
                        }
                    }

                    var pickedEffort = FleetPicker.Choose(
                        app, $"{title} · effort", ModelRows.EffortEntries, keymap, ModelRows.EffortIndex(was));

                    if (pickedEffort is null)
                    {
                        break;
                    }

                    var next = new RoleModel(model, ModelRows.EffortAt(pickedEffort.Value));

                    if (global)
                    {
                        Adapters.SaveHeadModel(next);
                    }
                    else
                    {
                        modelSettings.Save(project.Name, ModelRows.With(chosen, current, next));
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
                ManageRemotesView.Show(app, keymap, Adapters.Remotes(), Adapters.KnownRemotes(), Adapters.Forwards(), Adapters.Browser());
                break;

            case FleetAction.Notifications when EmbeddedWiring.HandBack(FleetActionIds.For(FleetAction.Notifications)):
                break;

            case FleetAction.Notifications:
                await ShowNotices(app, keymap, projects, project).ConfigureAwait(false);
                break;

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
                    var agent = await OpenEditorHandler.CallerAsync(
                            driver,
                            Environment.CurrentDirectory,
                            new ListAgentsHandler(Adapters.Agents()).Handle(project.Name))
                        .ConfigureAwait(false);

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

            case FleetAction.HideAllAgents:
                {
                    var hideMux = Adapters.Mux(Adapters.Log());

                    if (hideMux.Unsupported is not null)
                    {
                        FleetDialog.Error(app, "Hide all agents", hideMux.Unsupported);
                        break;
                    }

                    var hidden = await new HideAllAgentsHandler(hideMux.Driver, Adapters.Agents())
                        .HandleAsync(project.Name)
                        .ConfigureAwait(false);

                    Adapters.Log().Write(LogTag.For(project.Name, HideAllAgentsHandler.Summary(hidden)));
                    break;
                }

            case FleetAction.UpdateFleet:
                UpdateWiring.Install(app, FreshBuild.ThisProcess);
                break;

            case FleetAction.ShowVersion:
                {
                    var screen = FleetDialog.Wait(
                        app,
                        "fleet version",
                        ["Checking github.com for releases..."],
                        UpdateWiring.VersionScreenAsync);

                    if (ShowVersionView.Show(app, keymap, screen) is { } release)
                    {
                        UpdateWiring.Install(app, FreshBuild.ThisProcess, release == ShowVersionView.Latest ? null : release);
                    }

                    break;
                }

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

            case FleetAction.WhatsNew:
                ReleaseNotesView.Show(
                    app,
                    keymap,
                    ReleaseNotes.Group(ReleaseNotes.Parse(ReleaseNotes.Embedded())));

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


    private static async Task ShowNotices(IApplication app, Keymap keymap, IProjectStore projects, Project? current)
    {
        var noticeMux = Adapters.Mux(Adapters.Log()).Driver;
        var notices = Adapters.Notices();
        var inWindow = SwitchProjectHandler.Applies(noticeMux)
            ? (await ProjectsInWindow(noticeMux, projects.List()).ConfigureAwait(false)).Select(p => p.Name)
                .Concat(current is null ? [] : [current.Name]).ToList()
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

        var mover = new MoveProjectHandler(mux, Adapters.MainOrchestratorInNvim(target.Name), Adapters.Models(target.Name));

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
                project, AgentHarness.Orchestrator, Adapters.Executable, windowId, Adapters.MainOrchestratorInNvim(project.Name), Adapters.Models(project.Name)))
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

        await new RestoreSessionHandler(mux, Adapters.SubOrchestratorsInNvim(project.Name), Adapters.Agents(), Adapters.Models(project.Name))
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

        var dashboard = DashboardPane(panes, project.Root, Adapters.DashPane(project.Name));

        if (dashboard is not null)
        {
            await mux.FocusPaneAsync(dashboard.Id).ConfigureAwait(false);
        }
    }
}
