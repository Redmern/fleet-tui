using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Agents.CleanupAgents;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.MoveProject;
using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Diagnostics.ViewLogs;
using Fleet.Features.Files.BrowseFiles;
using Fleet.Features.Menu.EditFleetConfig;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Menu.EditSettings;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.QuitProject;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.ListRemotes;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
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
        FleetAction.BrowseFiles,
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

        if (requested is FleetAction.OpenProject or FleetAction.NewProject || project is null)
        {
            return await PickProjectCommand.RunAsync().ConfigureAwait(false);
        }

        var keymaps = Adapters.Keymaps();
        var adder = new AddRepositoryHandler(Adapters.Git());

        using IApplication app = FleetUi.Start();

        var keymap = new Keymap(keymaps.Load());

        var chosen = requested != FleetAction.None
            ? requested
            : FleetUi.Menu(app, keymap, MenuActions);

        if (chosen == FleetAction.OpenSettings)
        {
            chosen = FleetUi.Menu(app, keymap, SettingsActions);
        }

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
                var quitPanes = await quitMux.Driver.ListPanesAsync().ConfigureAwait(false);

                var quitSelf = Environment.GetEnvironmentVariable("WEZTERM_PANE");
                var quitWindow = quitPanes.FirstOrDefault(p => p.Id.Value == quitSelf)?.WindowId;

                var inWindow = ProjectsVisibleInWindow(projects.List(), quitPanes, quitWindow);

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
                    "Quit fleet",
                    "Just this project");

                if (quitChoice == DialogChoice.Cancelled)
                {
                    break;
                }

                if (quitChoice == DialogChoice.Primary)
                {
                    foreach (var toQuit in inWindow)
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

                var switchMux = Adapters.Mux(Adapters.Log());
                var panes = await switchMux.Driver.ListPanesAsync().ConfigureAwait(false);

                var self = Environment.GetEnvironmentVariable("WEZTERM_PANE");
                var currentWindow = panes.FirstOrDefault(p => p.Id.Value == self)?.WindowId;

                string Label(Project p) =>
                    panes.Any(x => PathKey.Same(x.Cwd, p.Root)) ? $"{p.Name}  (open)" : p.Name;

                var labels = others.Select(Label).ToList();

                var index = FleetPicker.Choose(app, "Switch project", labels, keymap);

                if (index is not { } chosenIndex)
                {
                    break;
                }

                var target = others[chosenIndex];

                var toPark = ProjectsVisibleInWindow(projects.List(), panes, currentWindow)
                    .Where(p => !string.Equals(
                        p.Name, target.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var mover = new MoveProjectHandler(switchMux.Driver);

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
                    await switchMux.Driver.FocusPaneAsync(dash.Id).ConfigureAwait(false);
                    break;
                }

                if (dash is null)
                {
                    await OpenProjectFlow(switchMux.Driver, target, currentWindow)
                        .ConfigureAwait(false);

                    break;
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

                if (!moved.Succeeded)
                {
                    FleetDialog.Error(app, "Switch project", moved.Error!);
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
                var current = aidlcSettings.Load(project.Name);

                var picked = FleetPicker.Choose(
                    app,
                    $"{SettingsDefaults.AidlcLabel} — {project.Name}",
                    [
                        new PickerEntry("off", "sub-orchestrators never get AIDLC guidance", "o"),
                        new PickerEntry("on", "every dispatch gets AIDLC guidance", "n"),
                        new PickerEntry("manual", "only when the prompt doubles the dispatch trigger", "m"),
                    ],
                    keymap,
                    (int)current.Aidlc);

                if (picked is not null)
                {
                    aidlcSettings.Save(project.Name, current.WithAidlcMode((AidlcMode)picked.Value));
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

                var configPane = await configMux.Driver.SpawnAsync(
                    new SpawnOptions
                    {
                        Cwd = project.Root,
                        SessionName = project.Name,
                        NewWindow = true,
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

    private static List<Project> ProjectsVisibleInWindow(
        IReadOnlyList<Project> allProjects, IReadOnlyList<Pane> panes, string? windowId) =>
        allProjects
            .Where(p => panes.Any(x => PathKey.Same(x.Cwd, p.Root)
                && x.WindowId == windowId
                && !string.Equals(
                    x.SessionName, FleetWorkspaces.Hidden, StringComparison.OrdinalIgnoreCase)))
            .ToList();

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
            .HandleAsync(new OpenProjectCommand(project, "claude", Adapters.Executable, windowId))
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return;
        }

        var agents = new ListAgentsHandler(Adapters.Agents()).Handle(project.Name);

        var runnable = agents
            .Where(a => Adapters.OnPath(AgentHarness.CommandFor(a.Harness)[0]))
            .ToList();

        await new RestoreSessionHandler(mux)
            .HandleAsync(project.Name, project.Root, runnable)
            .ConfigureAwait(false);

        await mux.FocusPaneAsync(result.Value.DashPane).ConfigureAwait(false);
    }

    private static async Task FocusMain(Project project)
    {
        var mux = Adapters.Mux(Adapters.Log());
        var panes = await mux.Driver.ListPanesAsync().ConfigureAwait(false);

        var dashboard = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, project.Root));

        if (dashboard is not null)
        {
            await mux.Driver.FocusPaneAsync(dashboard.Id).ConfigureAwait(false);
        }
    }
}
