using Fleet.Cli.Composition;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Projects.CreateProject;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.PickProject;
using Fleet.Features.Projects.PickProject.Models;
using Fleet.Ports.Projects.Models;
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
        FleetAction.EditKeybinds,
        FleetAction.Close,
    ];

    public static async Task<int> RunAsync()
    {
        var chosen = Choose();

        if (chosen is null)
        {
            return 0;
        }

        var log = Adapters.Log();
        var mux = Adapters.Mux(log);

        if (mux.Unsupported is not null)
        {
            return Fail(mux.Unsupported);
        }

        var result = await new OpenProjectHandler(mux.Driver)
            .HandleAsync(new OpenProjectCommand(chosen, "claude", Adapters.Executable))
            .ConfigureAwait(false);

        return result.Succeeded ? 0 : Fail(result.Error!);
    }

    private static Project? Choose()
    {
        var projects = Adapters.Projects();
        var keymaps = Adapters.Keymaps();
        var creator = new CreateProjectHandler(projects);

        using IApplication app = FleetUi.Start();

        var keymap = new Keymap(keymaps.Load());

        return PickProjectView.Show(
            app,
            keymap,
            new PickProjectHandler(projects),
            new PickProjectCallbacks(
                CreateProject: () => CreateProjectView.Show(app, creator),
                ShowMenu: () => FleetUi.Menu(app, keymap, MenuActions),
                EditKeybinds: () => EditKeybindsView.Show(app, keymaps, keymap)));
    }

    private static int Fail(string reason)
    {
        Console.Error.WriteLine($"fleet: {reason}");
        return 1;
    }
}
