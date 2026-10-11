using Fleet.Features.Todos.AddTodo.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;

namespace Fleet.Features.Todos.AddTodo;

public sealed class CaptureTodo(ITodoStore todos, IProjectStore projects)
{
    public Result<CapturedTodo> Handle(string text, Project? active, DateTimeOffset now)
    {
        var target = TodoPrompt.Split(text, name => projects.Load(name) is not null);
        var project = target.Project is { } named ? projects.Load(named) : active;

        if (project is null)
        {
            return Result<CapturedTodo>.Fail(
                "no project here to file the todo under; start it with the project's name, as in 'project: idea'.");
        }

        var added = new AddTodoHandler(todos).Handle(new AddTodoCommand(project.Root, target.Text), now);

        return added.Succeeded
            ? Result<CapturedTodo>.Ok(new CapturedTodo(project, added.Value))
            : Result<CapturedTodo>.Fail(added.Error!);
    }
}
