using Fleet.Shared.Results;
using Fleet.Shared.Todos;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Todos.ShowBoard;

public static class BoardRows
{
    public const int TitleWidth = 48;

    private const string NoEffort = "-";

    private const int EffortWidth = 3;

    private const int StatusWidth = 12;

    public static string Title(string project) => $"{project} — todo board";

    public const string Empty = "No tickets yet. Capture one with + in a main pane or fleet todo \"idea\".";

    public static async Task<Result> SafelyAsync(Func<Task<Result>> open)
    {
        try
        {
            return await open().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            return Result.Fail(e.Message);
        }
    }

    public static string EffortSaved(Ticket ticket) =>
        ticket.Effort is { } effort
            ? $"Effort of {ticket.Id} is {TicketWords.Of(effort)}."
            : $"Effort of {ticket.Id} is unset.";

    public static IReadOnlyList<FleetRow> For(IReadOnlyList<Ticket> tickets)
    {
        var idWidth = tickets.Count == 0 ? 0 : tickets.Max(t => t.Id.Length);

        return [.. tickets.Select(t => Row(t, idWidth))];
    }

    public static int IndexOf(IReadOnlyList<Ticket> tickets, string? id, int fallback)
    {
        if (tickets.Count == 0)
        {
            return 0;
        }

        for (var i = 0; i < tickets.Count; i++)
        {
            if (tickets[i].Id == id)
            {
                return i;
            }
        }

        return Math.Clamp(fallback, 0, tickets.Count - 1);
    }

    private static FleetRow Row(Ticket ticket, int idWidth)
    {
        var effort = TicketWords.Of(ticket.Effort) is { Length: > 0 } size ? size : NoEffort;

        return new(
            [
                FleetSpan.Muted(ticket.Id.PadRight(idWidth) + "  "),
                new FleetSpan(Cut(ticket.Title).PadRight(TitleWidth), ticket.Status == TicketStatus.Closed ? FleetTones.Muted : FleetTones.Normal),
            ],
            [
                new FleetSpan("  " + effort.PadRight(EffortWidth), FleetTones.Key),
                new FleetSpan(" " + TicketWords.Of(ticket.Status).PadRight(StatusWidth), Tone(ticket.Status)),
                FleetSpan.Muted(TicketWords.Of(ticket.Source)),
            ]);
    }

    private static string Cut(string title)
    {
        var line = title.ReplaceLineEndings(" ");

        return line.Length <= TitleWidth ? line : line[..(TitleWidth - 1)] + "…";
    }

    private static string Tone(TicketStatus status) => status switch
    {
        TicketStatus.InProgress => FleetTones.Warn,
        TicketStatus.Done => FleetTones.Good,
        TicketStatus.Closed => FleetTones.Muted,
        _ => FleetTones.Key,
    };
}
