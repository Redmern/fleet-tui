using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Hooks;
using DispatchRequest = Fleet.Features.Orchestrations.Dispatch.Models.DispatchCommand;

namespace Fleet.Cli.Commands;

public static class HookDispatchCommand
{
    public const string JsonModeVariable = "FLEET_HOOK_BLOCK";

    public static async Task<int> RunAsync(Invocation invocation)
    {
        var payload = Adapters.ReadHookPayload();

        var project = Resolve(invocation.Project, payload.Cwd);

        if (project is null)
        {
            return 0;
        }

        var trigger = Adapters.Settings().Load(project.Name).Trigger;
        var decision = HookPrompt.Intercepted(payload.Text, trigger);

        if (!decision.Take)
        {
            return 0;
        }

        return await BlockAsync(project, decision.Task).ConfigureAwait(false);
    }

    private static async Task<int> BlockAsync(Project project, string task)
    {
        var log = Adapters.Log();
        var mux = Adapters.Mux(log);

        string note;

        if (mux.Unsupported is not null)
        {
            note = $"fleet: could not dispatch — {mux.Unsupported}";
        }
        else
        {
            var reply = await new DispatchHandler(
                    mux.Driver, Adapters.Agents(), Adapters.HarnessConfig(),
                    history: Adapters.History(), namer: Adapters.SlugNamer())
                .HandleAsync(
                    new DispatchRequest(project.Name, project.Root, task),
                    DateTimeOffset.UtcNow.ToString("O"))
                .ConfigureAwait(false);

            note = reply.Succeeded ? reply.Value!.Note : $"fleet: could not dispatch — {reply.Error}";
            log.Write(LogTag.For(project.Name, reply.Succeeded
                ? $"dispatched sub-orchestrator {reply.Value!.Slug} via the hook"
                : $"hook dispatch failed: {reply.Error}"));
        }

        Emit(HookNote.For(note));
        return 2;
    }

    private static void Emit(string note)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(JsonModeVariable),
                "json",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.Out.Write(Adapters.HookBlockJson(note));
            return;
        }

        Console.Error.Write(note);
    }

    private static Project? Resolve(string? name, string? cwd) =>
        name is { } saved
            ? Adapters.Projects().Load(saved)
            : new ResolveProjectHandler(Adapters.Projects())
                .ForDirectory(cwd ?? Environment.CurrentDirectory);
}
