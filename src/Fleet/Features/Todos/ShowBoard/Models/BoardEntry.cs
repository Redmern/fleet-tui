using Fleet.Shared.Todos.Models;

namespace Fleet.Features.Todos.ShowBoard.Models;

public sealed record BoardEntry(BoardProject Project, Ticket Ticket)
{
    public bool Same(BoardEntry? other) =>
        other is not null && other.Project.Name == Project.Name && other.Ticket.Id == Ticket.Id;
}
