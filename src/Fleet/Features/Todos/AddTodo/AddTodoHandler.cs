using Fleet.Features.Todos.AddTodo.Models;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Features.Todos.AddTodo;

public sealed class AddTodoHandler(ITodoStore store)
{
    public const int MaxTitleLength = 80;

    public Result<Ticket> Handle(AddTodoCommand command, DateTimeOffset now)
    {
        var body = command.Text.Trim();
        var title = command.Title.Trim().Length > 0 ? command.Title.Trim() : FirstLine(body);

        if (title.Length == 0)
        {
            return Result<Ticket>.Fail("a todo needs a title or some text.");
        }

        var draft = new Ticket(
            string.Empty,
            title.Length > MaxTitleLength ? title[..MaxTitleLength].TrimEnd() : title,
            command.Effort,
            TicketStatus.Open,
            TicketSource.Local,
            now,
            body.Length == 0 ? string.Empty : body + "\n");

        return store.Add(command.ProjectRoot, draft);
    }

    private static string FirstLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;
}
