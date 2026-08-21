using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using DispatchRequest = Fleet.Features.Orchestrations.Dispatch.Models.DispatchCommand;

namespace Fleet.Cli.Commands;

public static class DispatchCommand
{
    public static async Task<int> RunAsync(Invocation invocation)
    {
        var project = Resolve(invocation.Project);

        if (project is null)
        {
            Console.Error.WriteLine("fleet dispatch: --project <name> is required");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(invocation.Text))
        {
            Console.Error.WriteLine("fleet dispatch: a task prompt is required");
            return 2;
        }

        var log = Adapters.Log();
        var mux = Adapters.Mux(log);

        if (mux.Unsupported is not null)
        {
            Console.Error.WriteLine($"fleet dispatch: {mux.Unsupported}");
            return 1;
        }

        var handler = new DispatchHandler(mux.Driver, Adapters.Agents(), Adapters.HarnessConfig(), history: Adapters.History());

        var reply = await handler
            .HandleAsync(
                new DispatchRequest(
                    project.Name, project.Root, invocation.Text, invocation.Caller ?? string.Empty),
                DateTimeOffset.UtcNow.ToString("O"))
            .ConfigureAwait(false);

        if (!reply.Succeeded)
        {
            Console.Error.WriteLine($"fleet dispatch: {reply.Error}");
            log.Write(LogTag.For(project.Name, $"dispatch failed: {reply.Error}"));
            return 1;
        }

        Console.WriteLine(reply.Value!.Note);
        log.Write(LogTag.For(project.Name, $"dispatched sub-orchestrator {reply.Value.Slug}"));
        return 0;
    }

    private static Project? Resolve(string? name) =>
        name is { } saved
            ? Adapters.Projects().Load(saved)
            : new ResolveProjectHandler(Adapters.Projects())
                .ForDirectory(Environment.CurrentDirectory);
}
