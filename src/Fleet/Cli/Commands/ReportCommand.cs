using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;

namespace Fleet.Cli.Commands;

public static class ReportCommand
{
    public static int Run(Invocation invocation)
    {
        var project = Resolve(invocation.Project);

        if (project is null)
        {
            Console.Error.WriteLine("fleet report: --project <name> is required");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(invocation.Caller))
        {
            Console.Error.WriteLine("fleet report: --caller <slug> is required");
            return 2;
        }

        var log = Adapters.Log();

        var handler = new ReportStatusHandler(Adapters.Agents());

        var result = handler.Handle(
            project.Name,
            invocation.Caller!,
            invocation.Status ?? string.Empty,
            invocation.Text ?? string.Empty);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"fleet report: {result.Error}");
            return 1;
        }

        Console.WriteLine(result.Value!);
        log.Write(LogTag.For(project.Name, result.Value!));
        return 0;
    }

    private static Project? Resolve(string? name) =>
        name is { } saved
            ? Adapters.Projects().Load(saved)
            : new ResolveProjectHandler(Adapters.Projects())
                .ForDirectory(Environment.CurrentDirectory);
}
