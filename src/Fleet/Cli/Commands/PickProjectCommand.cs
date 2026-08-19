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
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
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

        var chosen = Choose();

        if (chosen is null)
        {
            return 0;
        }

        var result = await new OpenProjectHandler(mux.Driver)
            .HandleAsync(new OpenProjectCommand(chosen, "claude", Adapters.Executable))
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

        Adapters.Workspaces().Submit(chosen.Name);

        return 0;
    }

    private static Project? Choose()
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
