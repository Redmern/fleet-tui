using Fleet.Cli.Composition;
using Fleet.Features.Diagnostics.RunDoctor;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Features.Setup.RunSetup;

namespace Fleet.Cli.Commands;

public static class DoctorCommand
{
    public static async Task<int> RunAsync()
    {
        var log = Adapters.Log();
        var mux = Adapters.Mux(log);
        var git = Adapters.Git();

        var handler = new RunDoctorHandler(mux.Driver, Adapters.Projects(), log, async () =>
        {
            var version = await git.RunAsync(Environment.CurrentDirectory, ["--version"])
                .ConfigureAwait(false);

            return version.Ok ? version.Out : null;
        });

        var report = await handler.HandleAsync(new RunDoctorCommand(mux.Name, mux.Unsupported))
            .ConfigureAwait(false);

        Print(report);

        return report.Healthy ? 0 : 1;
    }

    private static void Print(DoctorReport report)
    {
        Console.WriteLine("fleet doctor");
        Console.WriteLine($"  config        {Adapters.ConfigDirectory}");
        Console.WriteLine($"  mux driver    {report.ChosenDriver}");
        Console.WriteLine($"  mux reachable {(report.MuxReachable ? "yes" : "no")}");
        Console.WriteLine($"  git           {report.GitVersion ?? "NOT FOUND"}");

        foreach (var tool in SetupHandler.Required.Concat(SetupHandler.Harness).Where(t => t != "git"))
        {
            Console.WriteLine($"  {tool,-13} {(Adapters.OnPath(tool) ? "on PATH" : "NOT FOUND")}");
        }
        Console.WriteLine($"  projects      {report.Projects.Count}");

        foreach (var project in report.Projects)
        {
            Console.WriteLine($"                {project.Name} -> {project.Root}");
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
    }
}
