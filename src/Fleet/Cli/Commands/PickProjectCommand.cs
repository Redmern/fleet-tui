using Fleet.Cli.Composition;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Projects.CreateProject;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.PickProject;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Files.BrowseFiles;
using Fleet.Features.Projects.PickProject.Models;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Features.Projects.RemoveProject;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Enums;
using Terminal.Gui.App;

namespace Fleet.Cli.Commands;

public static class PickProjectCommand
{
    private static readonly FleetAction[] MenuActions =
    [
        FleetAction.NewProject,
        FleetAction.OpenProject,
        FleetAction.RemoveProject,
        FleetAction.EditKeybinds,
        FleetAction.Close,
    ];

    public static async Task<int> RunAsync()
    {
        var log = Adapters.Log();
        var mux = Adapters.Mux(log);

        if (mux.Unsupported is not null)
        {
            return Fail(mux.Unsupported);
        }

        var picked = Choose();

        if (picked is null)
        {
            return 0;
        }

        var chosen = picked.Project;
        var newWindow = picked.NewWindow;
        string? windowId = null;

        if (!newWindow)
        {
            var resolved = await ResolveWindowAsync(mux.Driver).ConfigureAwait(false);

            if (resolved is null)
            {
                return 0;
            }

            newWindow = resolved.Value.NewWindow;
            windowId = resolved.Value.WindowId;
        }

        var result = await new OpenProjectHandler(mux.Driver)
            .HandleAsync(new OpenProjectCommand(chosen, AgentHarness.Orchestrator, Adapters.Executable, windowId))
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Fail(result.Error!);
        }

        var agents = new ListAgentsHandler(Adapters.Agents()).Handle(chosen.Name);

        var runnable = agents
            .Where(a => Adapters.OnPath(AgentHarness.CommandFor(a.Harness)[0]))
            .ToList();

        foreach (var stranded in agents.Except(runnable)
            .Select(a => AgentHarness.CommandFor(a.Harness)[0])
            .Distinct())
        {
            Console.Error.WriteLine(
                $"fleet: {stranded} is not on PATH, so agents that open it stay closed.");
        }

        await new RestoreSessionHandler(mux.Driver)
            .HandleAsync(chosen.Name, chosen.Root, runnable)
            .ConfigureAwait(false);

        await mux.Driver.FocusPaneAsync(result.Value.DashPane).ConfigureAwait(false);

        if (!newWindow)
        {
            var self = mux.Driver.CurrentPane;

            if (!self.IsNone)
            {
                await mux.Driver.KillPaneAsync(self).ConfigureAwait(false);
            }
        }

        return 0;
    }

    private static async Task<(string? WindowId, bool NewWindow)?> ResolveWindowAsync(
        IMuxDriver mux)
    {
        var currentWindow = Adapters.CurrentWindow(mux);
        var panes = await mux.ListPanesAsync().ConfigureAwait(false);
        var windowPanes = panes.Where(p => p.WindowId == currentWindow).ToList();
        var projects = Adapters.Projects().List();

        var alreadyFleetWindow = windowPanes.Any(
            p => projects.Any(project => PathKey.Same(p.Cwd, project.Root)));

        if (alreadyFleetWindow || windowPanes.Count <= 1)
        {
            return (currentWindow, false);
        }

        var choice = PromptTakeOver(windowPanes.Count - 1);

        if (choice == DialogChoice.Cancelled)
        {
            return null;
        }

        if (choice == DialogChoice.Secondary)
        {
            return (null, true);
        }

        var self = mux.CurrentPane;

        foreach (var pane in windowPanes.Where(p => p.Id != self))
        {
            await mux.KillPaneAsync(pane.Id).ConfigureAwait(false);
        }

        return (currentWindow, false);
    }

    private static DialogChoice PromptTakeOver(int otherPaneCount)
    {
        using IApplication app = FleetUi.Start();

        var plural = otherPaneCount == 1 ? "pane" : "panes";

        return FleetDialog.Choose(
            app,
            "Open fleet here?",
            [
                $"This window has {otherPaneCount} other {plural} open.",
                "Opening fleet here closes them.",
            ],
            "Continue",
            "New window");
    }

    private static ProjectPick? Choose()
    {
        var projects = Adapters.Projects();
        var keymaps = Adapters.Keymaps();
        var creator = new CreateProjectHandler(projects);
        var remover = new RemoveProjectHandler(projects);

        var driver = Adapters.Mux(Adapters.Log()).Driver;

        Func<string, string?>? folders = null;

        if (Adapters.OnPath(FileBrowser.Command))
        {
            folders = wanted => Adapters.PickFolder(
                driver,
                "fleet",
                FileBrowser.StartIn(wanted, Directory.Exists, Adapters.HomeDirectory));
        }

        using IApplication app = FleetUi.Start();

        var keymap = new Keymap(keymaps.Load());

        return PickProjectView.Show(
            app,
            keymap,
            new PickProjectHandler(projects),
            new PickProjectCallbacks(
                CreateProject: () => CreateProjectView.Show(app, creator, folders),
                RemoveProject: project =>
                {
                    var confirmed = FleetDialog.Confirm(
                        app,
                        $"Remove {project.Name}?",
                        [
                            "fleet forgets this project.",
                            $"{project.Root} and everything in it stays on disk.",
                        ],
                        "Remove");

                    if (!confirmed)
                    {
                        return null;
                    }

                    var dropped = remover.Handle(project);

                    return dropped.Succeeded ? dropped.Value : dropped.Error;
                },
                ShowMenu: () => FleetUi.Menu(app, keymap, MenuActions),
                EditKeybinds: () => EditKeybindsView.Show(app, keymaps, keymap)));
    }

    private static int Fail(string reason)
    {
        Console.Error.WriteLine($"fleet: {reason}");
        return 1;
    }
}
