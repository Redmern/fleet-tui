using Fleet.Ports.Orchestrations;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Features.Todos.LaunchTicket;

public sealed class LaunchTicketHandler(ITodoStore store, IDispatcher dispatcher)
{
    public async Task<Result<Ticket>> HandleAsync(
        string project, string projectRoot, string id, CancellationToken ct = default)
    {
        if (store.Get(projectRoot, id) is not { } ticket)
        {
            return Result<Ticket>.Fail($"ticket {id} is gone.");
        }

        if (ticket.Status != TicketStatus.Open)
        {
            return Result<Ticket>.Fail(
                $"ticket {id} is {TicketWords.Of(ticket.Status)}; only open tickets launch.");
        }

        var dispatched = await DispatchAsync(project, projectRoot, PromptOf(ticket), ct).ConfigureAwait(false);

        if (!dispatched.Succeeded)
        {
            return Result<Ticket>.Fail(dispatched.Error!);
        }

        var slug = dispatched.Value!;
        var launched = store.Update(projectRoot, id, t => t with
        {
            Status = TicketStatus.InProgress,
            Orchestration = slug,
            Note = string.Empty,
        });

        return launched.Succeeded
            ? launched
            : Result<Ticket>.Fail($"dispatched {slug}, but ticket {id} could not be saved: {launched.Error}");
    }

    private static string PromptOf(Ticket ticket) =>
        string.IsNullOrWhiteSpace(ticket.Body) ? ticket.Title : ticket.Body;

    private async Task<Result<string>> DispatchAsync(
        string project, string projectRoot, string prompt, CancellationToken ct)
    {
        try
        {
            return await dispatcher.DispatchAsync(project, projectRoot, prompt, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Result<string>.Fail($"could not dispatch: {e.Message}");
        }
    }
}
