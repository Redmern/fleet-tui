using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Projects.ResolveProject;
using Fleet.Features.Todos.AddTodo;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;

namespace Fleet.Cli.Commands;

public static class TodoCommand
{
    public static int Run(Invocation invocation) => Run(invocation, Console.Out, Console.Error);

    public static int Run(Invocation invocation, TextWriter output, TextWriter error)
    {
        if (string.IsNullOrWhiteSpace(invocation.Text))
        {
            error.WriteLine("fleet todo: the idea's text is required");
            return 2;
        }

        Project? active = null;

        if (invocation.Project is { } named)
        {
            active = Adapters.Projects().Load(named);

            if (active is null)
            {
                error.WriteLine($"fleet todo: no project named '{named}'");
                return 2;
            }
        }
        else
        {
            active = new ResolveProjectHandler(Adapters.Projects()).ForDirectory(Environment.CurrentDirectory);
        }

        var captured = new CaptureTodo(Adapters.Todos(), Adapters.Projects())
            .Handle(invocation.Text, active, DateTimeOffset.UtcNow);

        if (!captured.Succeeded)
        {
            error.WriteLine($"fleet todo: {captured.Error}");
            return 1;
        }

        output.WriteLine(captured.Value.Note);
        Adapters.Log().Write(LogTag.For(captured.Value.Project.Name, $"added todo {captured.Value.Ticket.Id}"));
        return 0;
    }
}
