using Fleet.Shared.Results;
using Fleet.Shared.Todos;
using Fleet.Shared.Todos.Models;

namespace Fleet.Ports.Todos;

public interface ITodoStore
{
    IReadOnlyList<Ticket> List(string projectRoot);

    Ticket? Get(string projectRoot, string id);

    bool Exists(string projectRoot, string id) => Get(projectRoot, id) is not null;

    Result<Ticket> Add(string projectRoot, Ticket draft);

    Result Save(string projectRoot, Ticket ticket);

    Result<Ticket> Update(string projectRoot, string id, Func<Ticket, Ticket> change)
    {
        if (Get(projectRoot, id) is not { } current)
        {
            return Result<Ticket>.Fail($"ticket {id} is gone.");
        }

        var next = change(current);

        if (TicketFile.Format(next) == TicketFile.Format(current))
        {
            return Result<Ticket>.Ok(current);
        }

        var saved = Save(projectRoot, next);

        return saved.Succeeded ? Result<Ticket>.Ok(next) : Result<Ticket>.Fail(saved.Error!);
    }

    Ticket? FindByOrchestration(string projectRoot, string orchestration);
}
