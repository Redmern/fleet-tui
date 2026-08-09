using Fleet.Features.Agents.OpenAgent;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Ports.Agents;
using Fleet.Ports.Git;
using Fleet.Ports.Keymap;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Requests;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Composition;

public static class DashboardWiring
{
    private const string Harness = "claude";

    private static readonly FleetAction[] MenuActions =
    [
        FleetAction.NewAgent,
        FleetAction.AddRepository,
        FleetAction.Refresh,
        FleetAction.EditKeybinds,
        FleetAction.Close,
    ];

    public static DashboardCallbacks For(
        IApplication app,
        Project project,
        Keymap keymap,
        IKeymapStore keymaps,
        IGitRunner git,
        IMuxDriver mux,
        IAgentStore agents,
        IActionRequestStore requests)
    {
        var repositories = new ListRepositoriesHandler(git);
        var adder = new AddRepositoryHandler(git);
        var lister = new ListAgentsHandler(agents);
        var spawner = new NewAgentHandler(git, mux, agents);
        var opener = new OpenAgentHandler(mux);
        var branches = new ListBranchesHandler(git);

        return new DashboardCallbacks(
            LoadRepositories: async () =>
                (IReadOnlyList<RepositoryChoice>)(await repositories
                        .HandleAsync(project.Root).ConfigureAwait(false))
                    .Select(r => new RepositoryChoice(r.Name, r.Path, r.DefaultBranch))
                    .ToList(),

            AddRepository: async () =>
            {
                var request = AddRepositoryView.Show(app, project.Root);

                if (request is null)
                {
                    return null;
                }

                var outcome = await adder.HandleAsync(request).ConfigureAwait(false);
                return outcome.Succeeded ? null : outcome.Error;
            },

            ShowMenu: () => FleetUi.Menu(app, keymap, MenuActions),

            EditKeybinds: () => EditKeybindsView.Show(app, keymaps, keymap),

            TakeRequest: () => requests.TakePending(project.Name),

            LoadAgents: () =>
            {
                var running = lister.Handle(project.Name);
                return (AgentRows.For(running), running.Count);
            },

            NewAgent: async (available, selected) =>
            {
                var request = NewAgentView.Show(
                    app,
                    new NewAgentPrompt(
                        project.Name,
                        available.Select(r => (r.Name, r.Directory)).ToList(),
                        selected,
                        directory => branches.HandleAsync(directory).GetAwaiter().GetResult(),
                        Harness),
                    keymap);

                if (request is null)
                {
                    return null;
                }

                var outcome = await spawner.HandleAsync(request).ConfigureAwait(false);
                return outcome.Succeeded ? null : outcome.Error;
            },

            OpenAgent: async index =>
            {
                var running = lister.Handle(project.Name);

                if (index < 0 || index >= running.Count)
                {
                    return null;
                }

                var outcome = await opener.HandleAsync(project.Name, running[index])
                    .ConfigureAwait(false);

                return outcome.Succeeded ? null : outcome.Error;
            });
    }
}
