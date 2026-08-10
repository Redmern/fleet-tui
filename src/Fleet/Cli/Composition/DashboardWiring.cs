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
using Fleet.Features.Repositories;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.OpenRepository;
using Fleet.Features.Repositories.PullRepository;
using Fleet.Features.Repositories.RemoveRepository;
using Fleet.Features.Repositories.SetDefaultBranch;
using Fleet.Features.Repositories.RemoveRepository.Models;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
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
        FleetAction.RemoveAgent,
        FleetAction.AddRepository,
        FleetAction.RemoveRepository,
        FleetAction.Refresh,
        FleetAction.EditKeybinds,
        FleetAction.Close,
    ];

    private static string ChooseHarness(
        IApplication app,
        Keymap keymap,
        ChangeHarnessHandler harnesses,
        string project,
        AgentRecord agent)
    {
        var picked = FleetPicker.Choose(
            app,
            $"{agent.Repository}/{agent.Branch} opens",
            AgentHarness.All.Select(AgentHarness.Describe).ToList(),
            keymap,
            AgentHarness.All.ToList().IndexOf(agent.Harness));

        if (picked is null)
        {
            return string.Empty;
        }

        var wanted = AgentHarness.All[picked.Value];
        harnesses.Handle(project, agent, wanted);

        return $"{agent.Repository}/{agent.Branch} now opens {AgentHarness.Describe(wanted)} "
             + "next time it starts.";
    }

    private static string? ToggleHidden(
        IMuxDriver mux, HideAgentHandler hider, Project project, AgentRecord agent)
    {
        var panes = mux.ListPanesAsync().GetAwaiter().GetResult();

        var dashboard = panes
            .FirstOrDefault(p => PathKey.Same(p.Cwd, project.Root))?.WindowId;

        var outcome = hider
            .HandleAsync(project.Name, agent, dashboard)
            .GetAwaiter()
            .GetResult();

        if (!outcome.Succeeded)
        {
            return outcome.Error;
        }

        return outcome.Value!.Hidden
            ? $"{agent.Repository}/{agent.Branch} is hidden from the terminal; "
              + "it is still listed here."
            : $"{agent.Repository}/{agent.Branch} is back in the terminal.";
    }

    private static IReadOnlyList<string> RepositoryWarning(
        string name, string directory, RepositoryState state)
    {
        var lines = new List<string> { name, directory, string.Empty };

        if (!state.Exists)
        {
            lines.Add("It is already gone from disk.");
            return lines;
        }

        lines.Add("This deletes the repository and every worktree under it:");
        lines.AddRange(state.Worktrees.Take(5).Select(w => $"  {w}"));

        if (state.Worktrees.Count > 5)
        {
            lines.Add($"  ... and {state.Worktrees.Count - 5} more");
        }

        if (state.Unpushed.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"WARNING: {state.Unpushed.Count} branch(es) are not pushed:");
            lines.AddRange(state.Unpushed.Take(5).Select(b => $"  {b}"));
        }

        return lines;
    }

    private static IReadOnlyList<string> RemovalWarning(
        string repository, string branch, string worktree, WorktreeState state, bool deleting)
    {
        var lines = new List<string>
        {
            $"{repository}/{branch}",
            worktree,
            string.Empty,
        };

        if (!deleting)
        {
            lines.Add("The worktree and its branch stay on disk.");
            lines.Add("Only fleet forgets about this agent.");
            return lines;
        }

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
        var opener = new OpenAgentHandler(mux, agents);
        var hider = new HideAgentHandler(mux, agents);
        var branches = new ListBranchesHandler(git);
        var harnesses = new ChangeHarnessHandler(agents);
        var stopper = new StopAgentHandler(mux);
        var remover = new RemoveAgentHandler(git, mux, agents);
        var repositoryRemover = new RemoveRepositoryHandler(git);
        var puller = new PullRepositoryHandler(git);
        var defaults = new SetDefaultBranchHandler(git);
        var opener2 = new OpenRepositoryHandler(mux);
        var branches0 = branches;
        var states = new BranchStates(git);

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

                return (
                    AgentRows.For(running, a => states.For(a.Worktree, a.BaseRef)),
                    running.Count);
            },

            NewAgent: async (available, selected) =>
            {
                var request = NewAgentView.Show(
                    app,
                    new NewAgentPrompt(
                        project.Name,
                        available.Select(r => (r.Name, r.Directory, r.DefaultBranch)).ToList(),
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

                var outcome = await opener.HandleAsync(project.Name, running[index], project.Root)
                    .ConfigureAwait(false);

                return outcome.Succeeded ? null : outcome.Error;
            },

            ManageAgent: index =>
            {
                var running = lister.Handle(project.Name);

                if (index < 0 || index >= running.Count)
                {
                    return null;
                }

                var agent = running[index];

                var picked = FleetPicker.Choose(
                    app,
                    $"{agent.Repository}/{agent.Branch}",
                    AgentDisposal.Choices,
                    keymap);

                if (picked is null)
                {
                    return null;
                }

                if (picked == AgentDisposal.Opens)
                {
                    return ChooseHarness(app, keymap, harnesses, project.Name, agent);
                }

                if (picked == AgentDisposal.Hide)
                {
                    return ToggleHidden(mux, hider, project, agent);
                }

                if (picked == AgentDisposal.Stop)
                {
                    var stopped = stopper.HandleAsync(agent).GetAwaiter().GetResult();

                    return stopped.Succeeded
                        ? $"{agent.Repository}/{agent.Branch} stopped; its worktree is untouched."
                        : stopped.Error;
                }

                var deleting = picked == AgentDisposal.Delete;

                var state = deleting
                    ? remover.InspectAsync(agent).GetAwaiter().GetResult()
                    : WorktreeState.Gone;

                if (!FleetDialog.Confirm(
                        app,
                        deleting ? "Delete this worktree?" : "Remove this agent?",
                        RemovalWarning(agent.Repository, agent.Branch, agent.Worktree, state, deleting),
                        confirmText: deleting ? "Delete" : "Remove"))
                {
                    return null;
                }

                var outcome = remover
                    .HandleAsync(project.Name, agent, deleting)
                    .GetAwaiter()
                    .GetResult();

                if (!outcome.Succeeded)
                {
                    return outcome.Error;
                }

                return deleting
                    ? $"{agent.Repository}/{agent.Branch} removed with its worktree."
                    : $"{agent.Repository}/{agent.Branch} removed; its files are still on disk.";
            },

            RemoveRepository: repository =>
            {
                var owned = lister.Handle(project.Name)
                    .Where(a => a.Repository == repository.Name)
                    .ToList();

                if (owned.Count > 0)
                {
                    return $"{repository.Name} still has {owned.Count} agent(s). "
                         + "Remove those first.";
                }

                var state = repositoryRemover
                    .InspectAsync(repository.Directory)
                    .GetAwaiter()
                    .GetResult();

                if (!FleetDialog.Confirm(
                        app,
                        "Delete this repository?",
                        RepositoryWarning(repository.Name, repository.Directory, state),
                        confirmText: "Delete"))
                {
                    return null;
                }

                var outcome = repositoryRemover.Handle(repository.Directory);

                return outcome.Succeeded
                    ? $"{repository.Name} deleted."
                    : outcome.Error;
            },

            PullRepository: async repository =>
            {
                var outcome = await puller
                    .HandleAsync(repository.Directory, repository.DefaultBranch)
                    .ConfigureAwait(false);

                return outcome.Succeeded
                    ? $"{repository.Name}: {outcome.Value}"
                    : outcome.Error;
            },

            ManageRepository: repository =>
            {
                var picked = FleetPicker.Choose(
                    app, repository.Name, RepositoryChores.Choices, keymap);

                if (picked != RepositoryChores.DefaultBranch)
                {
                    return null;
                }

                var branches = branches0.HandleAsync(repository.Directory)
                    .GetAwaiter()
                    .GetResult()
                    .Where(b => !b.IsRemote)
                    .ToList();

                if (branches.Count == 0)
                {
                    return $"{repository.Name} has no local branches.";
                }

                var chosen = FleetPicker.Choose(
                    app,
                    $"{repository.Name} default branch",
                    branches.Select(b => b.Reference).ToList(),
                    keymap,
                    branches.FindIndex(b => b.Reference == repository.DefaultBranch));

                if (chosen is null)
                {
                    return null;
                }

                var wanted = branches[chosen.Value].Reference;

                var set = defaults
                    .HandleAsync(repository.Directory, wanted)
                    .GetAwaiter()
                    .GetResult();

                return set.Succeeded
                    ? $"{repository.Name} now defaults to {wanted}. Its worktrees are untouched."
                    : set.Error;
            },

            OpenRepository: async repository =>
            {
                var outcome = await opener2
                    .HandleAsync(
                        project.Name,
                        repository.Name,
                        repository.Directory,
                        repository.DefaultBranch,
                        project.Root)
                    .ConfigureAwait(false);

                return outcome.Succeeded ? null : outcome.Error;
            },

            AgentState: agent => states.For(agent.Worktree, agent.BaseRef));
    }
}
