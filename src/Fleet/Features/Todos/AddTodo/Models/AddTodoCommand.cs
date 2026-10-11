using Fleet.Shared.Todos.Enums;

namespace Fleet.Features.Todos.AddTodo.Models;

public sealed record AddTodoCommand(string ProjectRoot, string Text, string Title = "", TicketEffort? Effort = null);
