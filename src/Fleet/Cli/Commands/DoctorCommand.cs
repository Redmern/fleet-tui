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

        var embedded = await EmbeddedWiring.HealthAsync().ConfigureAwait(false);

        var report = await handler.HandleAsync(new RunDoctorCommand(mux.Name, mux.Unsupported, embedded))
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

            var claude = ClaudeWiring.Inspect(project.Root);

            Console.WriteLine(
                $"                  mcp: {(claude.ServerRegistered ? "registered" : "NOT registered")}, "
                + $"{(claude.ServerEnabled ? "enabled" : "NOT enabled")}, "
                + $"dispatch hook {(claude.HookInstalled ? "installed" : "NOT installed")}");
        }

        if (report.Projects.Count > 0)
        {
            Console.WriteLine(
                "  note          Claude Code (2.1.196+) only honours a project's mcp approval in a");
            Console.WriteLine(
                "                trusted workspace; fleet now trusts each folder it opens for you.");
        }

        if (report.Embedded is { } embedded)
        {
            PrintEmbedded(embedded);
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

    private static void PrintEmbedded(EmbeddedHealth embedded)
    {
        Console.WriteLine(
            $"  embedded      libghostty-vt {(embedded.Linked ? "linked" : "NOT linked: this build cannot run fleetd")}");

        if (embedded.Fleetd is { } fleetd)
        {
            Console.WriteLine(
                $"  fleetd        pid {fleetd.Pid}: {fleetd.Workspaces} workspace(s), {fleetd.Panes} pane(s), "
                + $"{fleetd.WarmMenus} warm menu(s), {fleetd.Clients} client(s) attached");
            Console.WriteLine(
                $"                {fleetd.Executable} ({(fleetd.SameBuild ? "this build" : "another build than this fleet; restart fleetd to switch")})");
        }
        else if (embedded.FleetdTooOld)
        {
            Console.WriteLine("  fleetd        running, on a build too old to report its status; restart fleetd to switch");
        }
        else
        {
            Console.WriteLine("  fleetd        not running (fleet attach starts it)");
        }

        if (embedded.Saved is { } saved)
        {
            Console.WriteLine(
                $"  saved session {saved.Workspaces} workspace(s), {saved.Panes} pane(s), saved {saved.SavedAt:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"                {saved.Path}");
        }
        else if (embedded.SavedError is null)
        {
            Console.WriteLine("  saved session none: a new fleetd starts empty");
        }
    }
}
