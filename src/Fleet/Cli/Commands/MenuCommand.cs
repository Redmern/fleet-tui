using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.OpenAgent;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Projects.QuitProject;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Commands;

public static class MenuCommand
{
    private static readonly FleetAction[] MenuActions =
    [
        FleetAction.QuitFleet,
        FleetAction.EditKeybinds,
        FleetAction.FocusMain,
        FleetAction.ListAgents,
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
                var request = AddRepositoryView.Show(app, project.Root);

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
                await Quit(project).ConfigureAwait(false);
                break;

            case FleetAction.FocusMain:
                await FocusMain(project).ConfigureAwait(false);
                break;

            case FleetAction.EditKeybinds:
                EditKeybindsView.Show(app, keymaps, keymap);
                break;

            case FleetAction.ListAgents:
                var agents = Adapters.Agents();
                var mux = Adapters.Mux(Adapters.Log());
                var opener = new OpenAgentHandler(mux.Driver, agents);
                var listing = AgentSplit.By(new ListAgentsHandler(agents).Handle(project.Name));

                ListAgentsView.Show(app, listing, keymap, (tab, index) =>
                {
                    var chosen = listing.For(tab);

                    if (index < 0 || index >= chosen.Count)
                    {
                        return null;
                    }

                    var outcome = opener
                        .HandleAsync(project.Name, chosen[index], project.Root)
                        .GetAwaiter()
                        .GetResult();

                    return outcome.Succeeded ? null : outcome.Error;
                });

                break;
        }

        return 0;
    }

    private static async Task Quit(Project project)
    {
        var agents = new ListAgentsHandler(Adapters.Agents()).Handle(project.Name);
        var mux = Adapters.Mux(Adapters.Log());

        await new QuitProjectHandler(mux.Driver)
            .HandleAsync(project.Root, agents)
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
