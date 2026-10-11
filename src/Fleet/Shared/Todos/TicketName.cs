using System.Globalization;
using Fleet.Shared.Orchestrations;

namespace Fleet.Shared.Todos;

public static class TicketName
{
    public static string For(int counter, string title) =>
        $"{counter.ToString("D4", CultureInfo.InvariantCulture)}-{OrchestrationSlug.Of(title)}";

    public static int NextCounter(IEnumerable<string> stems) =>
        stems.Select(Counter).Where(c => c is not null).Select(c => c!.Value).DefaultIfEmpty(0).Max() + 1;

    public static int? Counter(string stem)
    {
        var digits = 0;

        while (digits < stem.Length && char.IsAsciiDigit(stem[digits]))
        {
            digits++;
        }

        if (digits == 0 || digits >= stem.Length || stem[digits] != '-')
        {
            return null;
        }

        return int.TryParse(stem.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var counter)
            ? counter
            : null;
    }
}
