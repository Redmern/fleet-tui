using System.Globalization;
using Fleet.Features.Diagnostics.ViewLogs.Models;
using Fleet.Shared;

namespace Fleet.Features.Diagnostics.ViewLogs;

public static class LogParser
{
    public const string StampFormat = "MM-dd HH:mm:ss";

    public static IReadOnlyList<LogEntry> Parse(IReadOnlyList<string> lines)
    {
        var found = new List<(string Stamp, string Project, string Message, List<string> Details)>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            if (line.Length == 0)
            {
                continue;
            }

            if (Stamped(line) is { } head)
            {
                found.Add(head);
                continue;
            }

            if (found.Count > 0)
            {
                found[^1].Details.Add(line.Trim());
            }
        }

        return
        [
            .. found.Select(f => new LogEntry(f.Stamp, f.Project, f.Message, f.Details)),
        ];
    }

    public static IReadOnlyList<LogEntry> For(
        string project, IReadOnlyList<LogEntry> entries) =>
        [
            .. entries
                .Where(e => e.Project.Length == 0
                    || string.Equals(e.Project, project, StringComparison.OrdinalIgnoreCase))
                .Reverse(),
        ];

    private static (string Stamp, string Project, string Message, List<string> Details)? Stamped(
        string line)
    {
        var split = line.IndexOf(' ', StringComparison.Ordinal);

        if (split <= 0)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                line[..split],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var when))
        {
            return null;
        }

        var (project, message) = LogTag.Split(line[(split + 1)..]);

        return (
            when.ToLocalTime().ToString(StampFormat, CultureInfo.InvariantCulture),
            project,
            message,
            []);
    }
}
