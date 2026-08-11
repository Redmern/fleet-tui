using Fleet.Features.Diagnostics.ViewLogs.Models;
using Fleet.Ui.Models;

namespace Fleet.Features.Diagnostics.ViewLogs;

public static class LogRows
{
    public const string EmptyHint = "(nothing logged for this project yet)";

    public static IReadOnlyList<FleetRow> For(IReadOnlyList<LogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint)];
        }

        var stampWidth = entries.Max(e => e.Stamp.Length);

        return
        [
            .. entries.Select(e => new FleetRow(
                [
                    FleetSpan.Muted($"{e.Stamp.PadRight(stampWidth)}   "),
                    FleetSpan.Plain(e.Message),
                ],
                e.Details.Count == 0
                    ? null
                    : [FleetSpan.Muted($"{e.Details.Count} more ")])),
        ];
    }

    public static IReadOnlyList<FleetRow> Detail(LogEntry entry)
    {
        List<FleetRow> rows =
        [
            new([FleetSpan.Muted(entry.Stamp)]),
            FleetRow.Plain(entry.Message),
        ];

        if (entry.Details.Count == 0)
        {
            rows.Add(new FleetRow([FleetSpan.Muted(NoDetail)]));

            return rows;
        }

        rows.Add(FleetRow.Plain(string.Empty));
        rows.AddRange(entry.Details.Select(d => new FleetRow([FleetSpan.Muted(d)])));

        return rows;
    }

    public const string NoDetail = "(no further detail was recorded)";
}
