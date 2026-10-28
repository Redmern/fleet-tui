using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Features.Todos.AddTodo;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Hooks;
using Fleet.Shared.Settings;

namespace Fleet.Cli.Commands;

public static class HookTodoCommand
{
    public static int Run(Invocation invocation) => Run(invocation, Console.In, Console.Out);

    public static int Run(Invocation invocation, TextReader input, TextWriter output)
    {
        var payload = Adapters.ReadHookPayload(input);
        var active = Resolve(invocation.Project, payload.Cwd);

        var trigger = active is null
            ? SettingsDefaults.TodoTrigger
            : Adapters.Settings().Load(active.Name).MergedOverDefaults().TodoTrigger;

        var decision = HookPrompt.Intercepted(payload.Text, trigger);

        if (!decision.Take)
        {
            return 0;
        }

        var captured = new CaptureTodo(Adapters.Todos(), Adapters.Projects())
            .Handle(decision.Task, active, DateTimeOffset.UtcNow);

        if (captured.Succeeded)
        {
            Adapters.Log().Write(LogTag.For(
                captured.Value.Project.Name, $"added todo {captured.Value.Ticket.Id} via the hook"));
        }

        return Adapters.StopPrompt(HookNote.For(captured.Succeeded
            ? captured.Value.Note
            : $"fleet: could not add the todo — {captured.Error}"), output);
    }

    private static Project? Resolve(string? name, string? cwd) =>
        name is { } saved
            ? Adapters.Projects().Load(saved)
            : new ResolveProjectHandler(Adapters.Projects())
                .ForDirectory(cwd ?? Environment.CurrentDirectory);
}
