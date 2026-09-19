using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Agents.CleanupAgents;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.MoveProject;
using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Diagnostics.ViewLogs;
using Fleet.Features.Files.BrowseFiles;
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
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Commands;

public static class MenuCommand
{
    private const int LogTail = 400;

    private static readonly FleetAction[] MenuActions =
    [
        FleetAction.QuitFleet,
        FleetAction.EditKeybinds,
        FleetAction.FocusMain,
        FleetAction.SwitchProject,
        FleetAction.CombineWindows,
        FleetAction.ListAgents,
        FleetAction.ViewLogs,
        FleetAction.BrowseFiles,
        FleetAction.RebuildDashboard,
        FleetAction.EditSettings,
        FleetAction.CleanupProject,
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
                if (FleetDialog.Confirm(
                        app,
                        $"Quit fleet for {project.Name}?",
                        [],
                        "Quit"))
                {
                    await Quit(project).ConfigureAwait(false);
                }

                break;

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

                var moved = await new MoveProjectHandler(switchMux.Driver)
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

            case FleetAction.CombineWindows:
            {
                var allProjects = projects.List();
                var combineMux = Adapters.Mux(Adapters.Log());
                var combinePanes = await combineMux.Driver.ListPanesAsync().ConfigureAwait(false);

                var combineSelf = Environment.GetEnvironmentVariable("WEZTERM_PANE");
                var combineCurrentWindow = combinePanes
                    .FirstOrDefault(p => p.Id.Value == combineSelf)?.WindowId;

                var otherWindows = combinePanes
                    .Where(p => p.WindowId != combineCurrentWindow)
                    .GroupBy(p => p.WindowId)
                    .Select(group => new
                    {
                        group.Key,
                        Projects = allProjects
                            .Where(proj => group.Any(p => PathKey.Same(p.Cwd, proj.Root)))
                            .OrderBy(proj => proj.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                    })
                    .Where(w => w.Projects.Count > 0)
                    .ToList();

                if (otherWindows.Count == 0)
                {
                    FleetDialog.Error(app, "Combine windows", "No other fleet windows are open.");
                    break;
                }

                var windowLabels = otherWindows
                    .Select(w => string.Join(", ", w.Projects.Select(p => p.Name)))
                    .ToList();

                var windowIndex = FleetPicker.Choose(app, "Combine windows", windowLabels, keymap);

                if (windowIndex is not { } chosenWindowIndex)
                {
                    break;
                }

                var sourceWindow = otherWindows[chosenWindowIndex];

                foreach (var toMove in sourceWindow.Projects)
                {
                    var combineMoved = await new MoveProjectHandler(combineMux.Driver)
                        .HandleAsync(
                            toMove.Name,
                            toMove.Root,
                            new ListAgentsHandler(Adapters.Agents()).Handle(toMove.Name),
                            combineCurrentWindow,
                            Adapters.DashPane(toMove.Name),
                            combineSelf,
                            Adapters.Executable)
                        .ConfigureAwait(false);

                    if (!combineMoved.Succeeded)
                    {
                        FleetDialog.Error(app, "Combine windows", combineMoved.Error!);
                        break;
                    }
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
