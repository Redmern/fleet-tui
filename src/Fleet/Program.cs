using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Features.Diagnostics.RunDoctor;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Features.Projects.CreateProject;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.PickProject;
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
using Fleet.Ports.Mux;
using Fleet.Ports.Projects;
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
            "doctor" => await DoctorAsync().ConfigureAwait(false),
            "--help" or "-h" or "help" => Help(),
            var verb => Unknown(verb),
        };

    private static IFleetLog NewLog() => new FileLog();

    private static IProjectStore NewProjectStore() => new JsonProjectStore();

    private static IGitRunner NewGit() => new GitRunner();

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

        using (IApplication app = Application.Create().Init())
        {
            FleetTheme.Register();

            project = PickProjectView.Show(
                app,
                new PickProjectHandler(store),
                createProject: () => CreateProjectView.Show(app, creator));
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

        using IApplication app = Application.Create().Init();
        FleetTheme.Register();

        ShowDashboardView.Show(app, project.Name, new DashboardCallbacks(
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
            }));

        return 0;
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
