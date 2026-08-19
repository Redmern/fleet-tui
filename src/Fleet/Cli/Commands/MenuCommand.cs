using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Diagnostics.ViewLogs;
using Fleet.Features.Files.BrowseFiles;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Menu.EditSettings;
using Fleet.Features.Projects.QuitProject;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.ListRemotes;
using Fleet.Features.Repositories.ListRepositories;
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
        FleetAction.EditSettings,
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
                var switchMux = Adapters.Mux(Adapters.Log());
                var panes = await switchMux.Driver.ListPanesAsync().ConfigureAwait(false);

                var open = panes
                    .Select(p => p.SessionName)
                    .Where(w => !string.IsNullOrWhiteSpace(w)
                        && !string.Equals(w, FleetWorkspaces.Default, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(w, FleetWorkspaces.Hidden, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(w, project.Name, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (open.Count == 0)
                {
                    FleetDialog.Error(
                        app, "Switch project", "No other projects are open right now.");

                    break;
                }

                var picked = FleetPicker.Choose(app, "Switch project", open, keymap);

                if (picked is { } index)
                {
                    Adapters.Workspaces().Submit(open[index]);
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
