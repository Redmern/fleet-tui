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

        var normalized = OrchestrationStatus.Normalize(status);

        if (normalized is not (OrchestrationStatus.Done or OrchestrationStatus.Failed))
        {
            return;
        }

        store.Update(projectRoot, ticket.Id, current => Follows(current, orchestration)
            ? Next(current, normalized, orchestration, summary)
            : current);
    }

    private static bool Follows(Ticket ticket, string orchestration) =>
        ticket.Status == TicketStatus.InProgress
        && string.Equals(ticket.Orchestration, orchestration, StringComparison.OrdinalIgnoreCase);

    private static Ticket Next(Ticket ticket, string status, string orchestration, string summary) =>
        status == OrchestrationStatus.Done
            ? ticket with { Status = TicketStatus.Done }
            : ticket with { Status = TicketStatus.Open, Note = FailureNote(orchestration, summary) };
}
