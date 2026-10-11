using Fleet.Shared.Todos.Enums;

namespace Fleet.Shared.Todos.Models;

public sealed record Ticket(
    string Id,
    string Title,
    TicketEffort? Effort,
    TicketStatus Status,
    TicketSource Source,
    DateTimeOffset Created,
    string Body)
{
    public string Jira { get; init; } = string.Empty;

    public string Orchestration { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;
}
