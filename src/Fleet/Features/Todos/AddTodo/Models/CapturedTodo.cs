using Fleet.Ports.Projects.Models;
using Fleet.Shared.Todos.Models;

namespace Fleet.Features.Todos.AddTodo.Models;

public sealed record CapturedTodo(Project Project, Ticket Ticket)
{
    public string Note => $"fleet: added todo {Ticket.Id} to {Project.Name}";
}
