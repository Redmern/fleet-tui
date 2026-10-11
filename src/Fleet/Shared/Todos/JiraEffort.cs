using Fleet.Shared.Todos.Enums;

namespace Fleet.Shared.Todos;

public static class JiraEffort
{
    public static TicketEffort? From(double? points) => points switch
    {
        null => null,
        <= 1 => TicketEffort.XS,
        <= 2 => TicketEffort.S,
        <= 4 => TicketEffort.M,
        < 8 => TicketEffort.L,
        _ => TicketEffort.XL,
    };
}
