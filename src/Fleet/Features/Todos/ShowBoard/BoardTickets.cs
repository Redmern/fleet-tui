using Fleet.Features.Todos.ShowBoard.Models;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Features.Todos.ShowBoard;

public sealed class BoardTickets(ITodoStore store)
{
    public IReadOnlyList<Ticket> List(string projectRoot) =>
        [.. store.List(projectRoot).OrderBy(t => t.Status).ThenByDescending(t => t.Created).ThenByDescending(t => t.Id, StringComparer.Ordinal)];

    public IReadOnlyList<BoardEntry> List(IReadOnlyList<BoardProject> projects) =>
    [
        .. projects
            .SelectMany(p => Readable(p).Select(t => new BoardEntry(p, t)))
            .OrderBy(e => e.Ticket.Status)
            .ThenByDescending(e => e.Ticket.Created)
            .ThenByDescending(e => e.Ticket.Id, StringComparer.Ordinal)
            .ThenBy(e => e.Project.Name, StringComparer.OrdinalIgnoreCase),
    ];

    public Result<Ticket> CycleEffort(string projectRoot, string id) =>
        Change(projectRoot, id, t => t with { Effort = NextEffort(t.Effort) });

    public Result<Ticket> Close(string projectRoot, string id) =>
        Change(projectRoot, id, t => t with { Status = TicketStatus.Closed });

    public static TicketEffort? NextEffort(TicketEffort? effort) => effort switch
    {
        null => TicketEffort.XS,
        TicketEffort.XL => null,
        var size => size + 1,
    };

    private IReadOnlyList<Ticket> Readable(BoardProject project)
    {
        try
        {
            return store.List(project.Root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private Result<Ticket> Change(string projectRoot, string id, Func<Ticket, Ticket> change) =>
        store.Update(projectRoot, id, change);
}
