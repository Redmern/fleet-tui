using System.Text;
using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Features.Diagnostics.RunDoctor;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Projects.CreateProject;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.PickProject;
using Fleet.Features.Projects.PickProject.Models;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Platform.Git;
using Fleet.Platform.Logging;
using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Constants;
using Fleet.Platform.Mux.Models;
using Fleet.Platform.Mux.WezTerm;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Ports.Git;
using Fleet.Ports.Keymap;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet;

public static class Program
{
    public static async Task<int> Main(string[] args) =>
        (args.Length > 0 ? args[0] : string.Empty) switch
        {
            "" => await PickAndOpenAsync().ConfigureAwait(false),
            "dash" => Dash(args[1..]),
            "menu" => await MenuAsync(args[1..]).ConfigureAwait(false),
            "apply-keybinds" => ApplyKeybinds(),
            "doctor" => await DoctorAsync().ConfigureAwait(false),
            "--help" or "-h" or "help" => Help(),
            var verb => Unknown(verb),
        };

    private static IFleetLog NewLog() => new FileLog();

    private static IProjectStore NewProjectStore() => new JsonProjectStore();

    private static IGitRunner NewGit() => new GitRunner();

    private static IKeymapStore NewKeymapStore() => new JsonKeymapStore();

    private static IMuxDriver NewMux(IFleetLog log, out string chosen, out string? unsupported)
    {
        chosen = DriverSelector.Choose(MuxEnvironment.Current(MuxEnvironment.OnPath));

        unsupported = chosen == DriverNames.WezTerm
            ? null
            : $"the '{chosen}' driver is not implemented yet (phase 1 ships wezterm only)";

        return new FailSilentDriver(new WezTermDriver(), log.Swallowed);
    }

    private static async Task<int> PickAndOpenAsync()
    {
        var store = NewProjectStore();
        var creator = new CreateProjectHandler(store);

        Ports.Projects.Models.Project? project;

        var keymapStore = NewKeymapStore();

        using (IApplication app = Application.Create().Init())
        {
            FleetTheme.Register();

            var keymap = new Keymap(keymapStore.Load());

            project = PickProjectView.Show(
                app,
                keymap,
                new PickProjectHandler(store),
                new PickProjectCallbacks(
                    CreateProject: () => CreateProjectView.Show(app, creator),
                    ShowMenu: () => Menu(app, keymap,
                    [
                        FleetAction.NewProject,
                        FleetAction.OpenProject,
                        FleetAction.EditKeybinds,
                        FleetAction.Close,
                    ]),
                    EditKeybinds: () => EditKeybindsView.Show(app, keymapStore, keymap)));
        }

        if (project is null)
        {
            return 0;
        }

        var log = NewLog();
        var mux = NewMux(log, out _, out var unsupported);

        if (unsupported is not null)
        {
            Console.Error.WriteLine($"fleet: {unsupported}");
            return 1;
        }

        var result = await new OpenProjectHandler(mux).HandleAsync(new OpenProjectCommand(
                project,
                Harness: "claude",
                FleetExecutable: Environment.ProcessPath ?? "fleet"))
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"fleet: {result.Error}");
            return 1;
        }

        return 0;
    }

    private static int Dash(string[] args)
    {
        var name = ValueOf(args, "--project");

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("fleet dash: --project <name> is required");
            return 2;
        }

        var project = NewProjectStore().Load(name);

        if (project is null)
        {
            Console.Error.WriteLine($"fleet dash: no saved project '{name}'");
            return 1;
        }

        var git = NewGit();
        var lister = new ListRepositoriesHandler(git);
        var adder = new AddRepositoryHandler(git);

        var keymapStore = NewKeymapStore();

        WezTermUserVars.MarkDashboard();

        using IApplication app = Application.Create().Init();
        FleetTheme.Register();

        var keymap = new Keymap(keymapStore.Load());

        ShowDashboardView.Show(app, project.Name, keymap, new DashboardCallbacks(
            LoadRepositories: async () =>
                (await lister.HandleAsync(project.Root).ConfigureAwait(false))
                    .Select(r => (r.Name, r.DefaultBranch))
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

            ShowMenu: () => Menu(app, keymap,
            [
                FleetAction.AddRepository,
                FleetAction.Refresh,
                FleetAction.EditKeybinds,
                FleetAction.Close,
            ]),

            EditKeybinds: () => EditKeybindsView.Show(app, keymapStore, keymap)));

        return 0;
    }

    private static FleetAction Menu(
        IApplication app, Keymap keymap, IReadOnlyList<FleetAction> actions)
    {
        var handler = new ShowMenuHandler(keymap);
        return ShowMenuView.Show(app, keymap, handler.Items(actions));
    }

    private static async Task<int> MenuAsync(string[] args)
    {
        var store = NewProjectStore();

        var project = ValueOf(args, "--project") is { } named && named.Length > 0
            ? store.Load(named)
            : new ResolveProjectHandler(store).ForDirectory(Environment.CurrentDirectory);

        var requested = ValueOf(args, "--action") is { } id && id.Length > 0
            ? FleetActionIds.Parse(id)
            : FleetAction.None;

        if (requested is FleetAction.OpenProject or FleetAction.NewProject || project is null)
        {
            return await PickAndOpenAsync().ConfigureAwait(false);
        }

        var keymapStore = NewKeymapStore();
        var git = NewGit();
        var adder = new AddRepositoryHandler(git);

        using IApplication app = Application.Create().Init();
        FleetTheme.Register();

        var keymap = new Keymap(keymapStore.Load());

        var chosen = requested != FleetAction.None
            ? requested
            : Menu(app, keymap,
            [
                FleetAction.AddRepository,
                FleetAction.EditKeybinds,
            ]);

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

            case FleetAction.EditKeybinds:
                EditKeybindsView.Show(app, keymapStore, keymap);
                break;
        }

        return 0;
    }

    private static int ApplyKeybinds()
    {
        var keymap = new Keymap(NewKeymapStore().Load());
        var exe = Environment.ProcessPath ?? "fleet";

        var lua = WezTermKeybinds.Generate(keymap, exe);
        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wezterm",
            "fleet.lua");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, lua);

        Console.WriteLine($"wrote {target}");
        Console.WriteLine($"  prefix chord  {keymap.PrefixDisplay}");
        Console.WriteLine($"  reload        {TouchWezTermConfig()}");
        Console.WriteLine();
        Console.WriteLine("add these two lines to your .wezterm.lua, then reload wezterm:");
        Console.WriteLine();
        Console.WriteLine("  local fleet = require 'fleet'");
        Console.WriteLine("  fleet.apply(config)");
        Console.WriteLine();
        Console.WriteLine("wezterm must be able to find fleet.lua, so ensure ~/.wezterm is on");
        Console.WriteLine("package.path, or copy fleet.lua next to your .wezterm.lua.");

        return 0;
    }

    private static string TouchWezTermConfig()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] candidates =
        [
            Path.Combine(home, ".wezterm.lua"),
            Path.Combine(home, ".config", "wezterm", "wezterm.lua"),
        ];

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                File.SetLastWriteTimeUtc(candidate, DateTime.UtcNow);
                return $"nudged {candidate}";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return $"could not nudge {candidate}: {e.Message}";
            }
        }

        return "no wezterm config found; reload wezterm yourself";
    }

    private static async Task<int> DoctorAsync()
    {
        var log = NewLog();
        var mux = NewMux(log, out var chosen, out var unsupported);
        var git = NewGit();

        var handler = new RunDoctorHandler(mux, NewProjectStore(), log, async () =>
        {
            var r = await git.RunAsync(Environment.CurrentDirectory, ["--version"])
                .ConfigureAwait(false);

            return r.Ok ? r.Out : null;
        });

        var report = await handler.HandleAsync(new RunDoctorCommand(chosen, unsupported))
            .ConfigureAwait(false);

        Console.WriteLine("fleet doctor");
        Console.WriteLine($"  config        {FleetPaths.Config}");
        Console.WriteLine($"  mux driver    {report.ChosenDriver}");
        Console.WriteLine($"  mux reachable {(report.MuxReachable ? "yes" : "no")}");
        Console.WriteLine($"  git           {report.GitVersion ?? "NOT FOUND"}");
        Console.WriteLine($"  projects      {report.Projects.Count}");

        foreach (var p in report.Projects)
        {
            Console.WriteLine($"                {p.Name} -> {p.Root}");
        }

        if (report.RecentSwallowed.Count > 0)
        {
            Console.WriteLine("  recent swallowed failures:");
            foreach (var line in report.RecentSwallowed)
            {
                Console.WriteLine($"                {line}");
            }
        }

        foreach (var problem in report.Problems)
        {
            Console.WriteLine($"  ! {problem}");
        }

        Console.WriteLine(report.Healthy ? "OK" : $"{report.Problems.Count} problem(s)");
        return report.Healthy ? 0 : 1;
    }

    private static string? ValueOf(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Help()
    {
        Console.WriteLine("""
            fleet - orchestration for AI coding agents

            usage:
              fleet                       pick a project and open it
              fleet dash --project <name> the dashboard (runs inside a pane)
              fleet menu                  the fleet menu, or the picker outside a project
              fleet menu --action <id>    jump straight to add-repository or keybinds
              fleet apply-keybinds        write the wezterm keybinding module
              fleet doctor                check the environment
            """);
        return 0;
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'. Try 'fleet --help'.");
        return 2;
    }
}
