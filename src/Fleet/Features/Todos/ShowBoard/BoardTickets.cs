using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Features.Todos.ShowBoard;

public sealed class BoardTickets(ITodoStore store)
{
    public IReadOnlyList<Ticket> List(string projectRoot) =>
        [.. store.List(projectRoot).OrderBy(t => t.Status).ThenByDescending(t => t.Created).ThenByDescending(t => t.Id, StringComparer.Ordinal)];

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

    private Result<Ticket> Change(string projectRoot, string id, Func<Ticket, Ticket> change)
    {
        if (store.Get(projectRoot, id) is not { } current)
        {
            return Result<Ticket>.Fail($"ticket {id} is gone.");
        }

        var next = change(current);
        var saved = store.Save(projectRoot, next);

        return saved.Succeeded ? Result<Ticket>.Ok(next) : Result<Ticket>.Fail(saved.Error!);
    }
}
