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

                string Label(Project p)
                {
                    var dash = panes.FirstOrDefault(x => PathKey.Same(x.Cwd, p.Root));

                    if (dash is null)
                    {
                        return p.Name;
                    }

                    var where = string.Equals(
                            dash.SessionName,
                            FleetWorkspaces.Hidden,
                            StringComparison.OrdinalIgnoreCase)
                        ? "hidden"
                        : currentWindow is not null && dash.WindowId == currentWindow
                            ? "this window"
                            : "another window";

                    return $"{p.Name}  (open · {where})";
                }

                var labels = others.Select(Label).ToList();

                var picked = FleetPicker.Choose(app, "Switch project", labels, keymap);

                if (picked is not { } index)
                {
                    break;
                }

                var target = others[index];
                var dash = panes.FirstOrDefault(x => PathKey.Same(x.Cwd, target.Root));
                var parked = dash is not null
                    && string.Equals(
                        dash.SessionName, FleetWorkspaces.Hidden, StringComparison.OrdinalIgnoreCase);
                var here = dash is not null
                    && !parked
                    && currentWindow is not null
                    && dash.WindowId == currentWindow;

                IReadOnlyList<string> options = dash is null
                    ? ["Open in this window", "Open in a new window"]
                    : parked
                        ? ["Show it in this window", "Show it in a new window"]
                        : here
                            ? ["Focus it", "Move it to a new window"]
                            : [
                                "Move it into this window",
                                "Move it to a new window",
                                "Focus its window",
                            ];

                var chosen2 = FleetPicker.Choose(app, target.Name, options, keymap);

                if (chosen2 is not { } action)
                {
                    break;
                }

                var focus = dash is not null && !parked && (here ? action == 0 : action == 2);

                if (focus)
                {
                    await switchMux.Driver.FocusPaneAsync(dash!.Id).ConfigureAwait(false);
                    break;
                }

                var intoThisWindow = !here && action == 0;
                var mover = new MoveProjectHandler(switchMux.Driver);

                if (intoThisWindow)
                {
                    await mover
                        .ParkAsync(
                            project.Name,
                            project.Root,
                            new ListAgentsHandler(Adapters.Agents()).Handle(project.Name),
                            Adapters.DashPane(project.Name),
                            self)
                        .ConfigureAwait(false);
                }

                if (dash is null)
                {
                    await OpenProjectFlow(
                            switchMux.Driver, target, intoThisWindow ? currentWindow : null)
                        .ConfigureAwait(false);

                    break;
                }

                var moved = await mover
                    .HandleAsync(
                        target.Name,
                        target.Root,
                        new ListAgentsHandler(Adapters.Agents()).Handle(target.Name),
                        intoThisWindow ? currentWindow : null,
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
