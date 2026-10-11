using Fleet.Shared.Results;
using Fleet.Shared.Todos.Models;

namespace Fleet.Ports.Todos;

public interface ITodoStore
{
    IReadOnlyList<Ticket> List(string projectRoot);

    Ticket? Get(string projectRoot, string id);

    Result<Ticket> Add(string projectRoot, Ticket draft);

    Result Save(string projectRoot, Ticket ticket);

    Ticket? FindByOrchestration(string projectRoot, string orchestration);
}
