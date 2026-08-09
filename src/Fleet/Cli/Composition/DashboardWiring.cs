using Fleet.Features.Agents.ChangeHarness;
using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Agents.OpenAgent;
using Fleet.Features.Agents.RemoveAgent;
using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Features.Agents.StopAgent;
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
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Composition;

public static class DashboardWiring
{
    private static readonly FleetAction[] MenuActions =
    [
        FleetAction.NewAgent,
        FleetAction.ChangeHarness,
        FleetAction.ToggleHidden,
        FleetAction.StopAgent,
        FleetAction.RemoveAgent,
        FleetAction.AddRepository,
        FleetAction.Refresh,
        FleetAction.EditKeybinds,
        FleetAction.Close,
    ];

    private static IReadOnlyList<string> RemovalWarning(
        string repository, string branch, string worktree, WorktreeState state)
    {
        var lines = new List<string>
        {
            $"{repository}/{branch}",
            worktree,
            string.Empty,
        };

        if (!state.Exists)
        {
            lines.Add("Its worktree is already gone; only the record is removed.");
            return lines;
        }

        if (state.IsDirty)
        {
            lines.Add($"WARNING: {state.Changed.Count} uncommitted change(s) will be lost:");
            lines.AddRange(state.Changed.Take(5).Select(c => $"  {c}"));

            if (state.Changed.Count > 5)
            {
                lines.Add($"  ... and {state.Changed.Count - 5} more");
            }
        }
        else
        {
            lines.Add("The worktree is clean. Its branch is kept.");
        }

        return lines;
    }

    public static DashboardCallbacks For(
        IApplication app,
        Project project,
        Keymap keymap,
        IKeymapStore keymaps,
        IGitRunner git,
        IMuxDriver mux,
        IAgentStore agents,
        IActionRequestStore requests,
        IWorkspaceRequestStore workspaces)
    {
        var repositories = new ListRepositoriesHandler(git);
        var adder = new AddRepositoryHandler(git);
        var lister = new ListAgentsHandler(agents);
        var spawner = new NewAgentHandler(git, mux, agents);
        var opener = new OpenAgentHandler(mux, workspaces);
        var hider = new HideAgentHandler(mux, agents);
        var branches = new ListBranchesHandler(git);
        var harnesses = new ChangeHarnessHandler(agents);
        var stopper = new StopAgentHandler(mux);
        var remover = new RemoveAgentHandler(git, mux, agents);

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
                        AgentHarness.Claude),
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
            },

            ChangeHarness: index =>
            {
                var running = lister.Handle(project.Name);

                if (index < 0 || index >= running.Count)
                {
                    return null;
                }

                var agent = running[index];

                var picked = FleetPicker.Choose(
                    app,
                    $"{agent.Repository}/{agent.Branch} opens",
                    AgentHarness.All.Select(AgentHarness.Describe).ToList(),
                    keymap,
                    AgentHarness.All.ToList().IndexOf(agent.Harness));

                if (picked is null)
                {
                    return null;
                }

                harnesses.Handle(project.Name, agent, AgentHarness.All[picked.Value]);

                return $"{agent.Repository}/{agent.Branch} now opens "
                     + $"{AgentHarness.Describe(AgentHarness.All[picked.Value])} next time it starts.";
            },

            ToggleHidden: async index =>
            {
                var running = lister.Handle(project.Name);

                if (index < 0 || index >= running.Count)
                {
                    return null;
                }

                var panes = await mux.ListPanesAsync().ConfigureAwait(false);

                var dashboard = panes.FirstOrDefault(
                    p => PathKey.Same(p.Cwd, project.Root))?.WindowId;

                var outcome = await hider
                    .HandleAsync(project.Name, running[index], dashboard)
                    .ConfigureAwait(false);

                if (!outcome.Succeeded)
                {
                    return outcome.Error;
                }

                var agent = outcome.Value!;

                return agent.Hidden
                    ? $"{agent.Repository}/{agent.Branch} is hidden from the terminal; "
                      + "it is still listed here."
                    : $"{agent.Repository}/{agent.Branch} is back in the terminal.";
            },

            StopAgent: async index =>
            {
                var running = lister.Handle(project.Name);

                if (index < 0 || index >= running.Count)
                {
                    return null;
                }

                var agent = running[index];
                var outcome = await stopper.HandleAsync(agent).ConfigureAwait(false);

                return outcome.Succeeded
                    ? $"{agent.Repository}/{agent.Branch} stopped; its worktree is untouched."
                    : outcome.Error;
            },

            RemoveAgent: index =>
            {
                var running = lister.Handle(project.Name);

                if (index < 0 || index >= running.Count)
                {
                    return null;
                }

                var agent = running[index];
                var state = remover.InspectAsync(agent).GetAwaiter().GetResult();

                if (!FleetDialog.Confirm(
                        app,
                        "Remove this agent?",
                        RemovalWarning(agent.Repository, agent.Branch, agent.Worktree, state),
                        confirmText: "Remove"))
                {
                    return null;
                }

                var outcome = remover.HandleAsync(project.Name, agent).GetAwaiter().GetResult();

                return outcome.Succeeded
                    ? $"{agent.Repository}/{agent.Branch} removed."
                    : outcome.Error;
            });
    }
}
