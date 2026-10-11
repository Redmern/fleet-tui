using Fleet.Ports.Todos;
using Fleet.Shared.Constants;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Features.Todos.LaunchTicket;

public sealed class TicketFollowUp(ITodoStore store) : ITicketStatusSink
{
    public void Reported(string projectRoot, string orchestration, string status, string summary)
    {
        try
        {
            Follow(projectRoot, orchestration, status, summary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string FailureNote(string orchestration, string summary) =>
        summary.Trim().Length == 0 ? $"{orchestration} failed" : $"{orchestration} failed: {summary.Trim()}";

    private void Follow(string projectRoot, string orchestration, string status, string summary)
    {
        if (store.FindByOrchestration(projectRoot, orchestration) is not { Status: TicketStatus.InProgress } ticket)
        {
            return;
        }

        Ticket? next = OrchestrationStatus.Normalize(status) switch
        {
            OrchestrationStatus.Done => ticket with { Status = TicketStatus.Done },
            OrchestrationStatus.Failed => ticket with
            {
                Status = TicketStatus.Open,
                Note = FailureNote(orchestration, summary),
            },
            _ => null,
        };

        if (next is not null)
        {
            store.Save(projectRoot, next);
        }
    }
}
