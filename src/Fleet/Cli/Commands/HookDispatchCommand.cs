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
    public static Task<int> RunAsync(Invocation invocation) => RunAsync(invocation, Console.In, Console.Out);

    public static async Task<int> RunAsync(Invocation invocation, TextReader input, TextWriter output)
    {
        var payload = Adapters.ReadHookPayload(input);

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

        return await StopAsync(project, decision.Task, output).ConfigureAwait(false);
    }

    private static async Task<int> StopAsync(Project project, string task, TextWriter output)
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
                    history: Adapters.History(), namer: Adapters.SlugNamer(), settings: Adapters.Settings(), intents: Adapters.Intents(),
                    router: Adapters.ModelRouter(), log: log)
                .HandleAsync(
                    new DispatchRequest(project.Name, project.Root, task),
                    DateTimeOffset.UtcNow.ToString("O"))
                .ConfigureAwait(false);

            note = reply.Succeeded ? reply.Value!.Note : $"fleet: could not dispatch — {reply.Error}";
            log.Write(LogTag.For(project.Name, reply.Succeeded
                ? $"dispatched sub-orchestrator {reply.Value!.Slug} via the hook"
                : $"hook dispatch failed: {reply.Error}"));
        }

        return Adapters.StopPrompt(HookNote.For(note), output);
    }

    private static Project? Resolve(string? name, string? cwd) =>
        name is { } saved
            ? Adapters.Projects().Load(saved)
            : new ResolveProjectHandler(Adapters.Projects())
                .ForDirectory(cwd ?? Environment.CurrentDirectory);
}
