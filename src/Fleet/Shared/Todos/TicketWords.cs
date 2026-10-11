using Fleet.Shared.Todos.Enums;

namespace Fleet.Shared.Todos;

public static class TicketWords
{
    public static string Of(TicketEffort? effort) => effort?.ToString() ?? string.Empty;

    public static string Of(TicketStatus status) => status switch
    {
        TicketStatus.InProgress => "in-progress",
        TicketStatus.Done => "done",
        TicketStatus.Closed => "closed",
        _ => "open",
    };

    public static string Of(TicketSource source) => source == TicketSource.Jira ? "jira" : "local";

    public static TicketEffort? Effort(string text) => text.Trim().ToUpperInvariant() switch
    {
        "XS" => TicketEffort.XS,
        "S" => TicketEffort.S,
        "M" => TicketEffort.M,
        "L" => TicketEffort.L,
        "XL" => TicketEffort.XL,
        _ => null,
    };

    public static TicketStatus Status(string text) => text.Trim().ToLowerInvariant() switch
    {
        "in-progress" or "inprogress" or "in progress" => TicketStatus.InProgress,
        "done" => TicketStatus.Done,
        "closed" => TicketStatus.Closed,
        _ => TicketStatus.Open,
    };

    public static TicketSource Source(string text) =>
        text.Trim().Equals("jira", StringComparison.OrdinalIgnoreCase) ? TicketSource.Jira : TicketSource.Local;
}
