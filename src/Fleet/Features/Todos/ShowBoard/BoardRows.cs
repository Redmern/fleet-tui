using Fleet.Features.Todos.ShowBoard.Models;
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

    public static async Task<Result<T>> SafelyAsync<T>(Func<Task<Result<T>>> run)
    {
        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            return Result<T>.Fail(e.Message);
        }
    }

    public const string JiraSyncing = "Jira: pulling issues...";

    public static string JiraSynced(Result<int> pulled) =>
        pulled.Succeeded ? $"Jira: synced {pulled.Value}." : $"Jira: {pulled.Error} (showing cached tickets)";

    public static string JiraSynced(IReadOnlyList<(string Project, Result<int> Pulled)> pulls)
    {
        if (pulls.Count == 1)
        {
            return JiraSynced(pulls[0].Pulled);
        }

        var parts = pulls.Select(p => p.Pulled.Succeeded
            ? $"{p.Project} synced {p.Pulled.Value}"
            : $"{p.Project}: {p.Pulled.Error}");

        var failed = pulls.Where(p => !p.Pulled.Succeeded).Select(p => p.Project).ToList();

        var cached = failed.Count == 0 ? string.Empty : $" (showing cached tickets for {string.Join(", ", failed)})";

        return $"Jira: {string.Join("; ", parts)}{cached}.";
    }

    public static string Launched(Ticket ticket) =>
        $"Launched {ticket.Id} as sub-orchestrator {ticket.Orchestration}.";

    public static string EffortSaved(Ticket ticket) =>
        ticket.Effort is { } effort
            ? $"Effort of {ticket.Id} is {TicketWords.Of(effort)}."
            : $"Effort of {ticket.Id} is unset.";

    public static IReadOnlyList<FleetRow> For(IReadOnlyList<Ticket> tickets)
    {
        var idWidth = tickets.Count == 0 ? 0 : tickets.Max(t => t.Id.Length);

        return [.. tickets.Select(t => Row(t, idWidth))];
    }

    public static IReadOnlyList<FleetRow> For(IReadOnlyList<BoardEntry> entries, bool showProject)
    {
        var rows = For([.. entries.Select(e => e.Ticket)]);

        if (!showProject)
        {
            return rows;
        }

        var projectWidth = entries.Count == 0 ? 0 : entries.Max(e => e.Project.Name.Length);

        return [.. rows.Select((row, i) => row with { Spans = [FleetSpan.Muted(entries[i].Project.Name.PadRight(projectWidth) + "  "), .. row.Spans] })];
    }

    public static int IndexOf(IReadOnlyList<BoardEntry> entries, BoardEntry? keep, int fallback)
    {
        if (entries.Count == 0)
        {
            return 0;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Same(keep))
            {
                return i;
            }
        }

        return Math.Clamp(fallback, 0, entries.Count - 1);
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
