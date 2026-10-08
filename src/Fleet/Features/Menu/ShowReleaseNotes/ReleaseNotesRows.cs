using System.Text;
using Fleet.Features.Menu.ShowReleaseNotes.Models;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Menu.ShowReleaseNotes;

public static class ReleaseNotesRows
{
    public const string EmptyHint = "(no release notes in this build)";

    public const int WrapWidth = 68;

    public const string LegacyLabel = "   before the restart";

    private const string BulletMark = "  • ";

    private const string Continued = "    ";

    public static IReadOnlyList<FleetRow> For(IReadOnlyList<ReleaseGroup> groups)
    {
        if (groups.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint)];
        }

        var rows = new List<FleetRow>();

        foreach (var group in groups)
        {
            if (rows.Count > 0)
            {
                rows.Add(FleetRow.Plain(string.Empty));
            }

            rows.Add(new FleetRow(
            [
                new FleetSpan($"v{group.Minor}", FleetTones.Title),
                .. group.Legacy ? [FleetSpan.Muted(LegacyLabel)] : Array.Empty<FleetSpan>(),
            ]));

            foreach (var entry in group.Entries)
            {
                rows.Add(FleetRow.Plain(string.Empty));
                rows.Add(new FleetRow(
                [
                    new FleetSpan(entry.Version, FleetTones.Key),
                    .. entry.Date is null ? Array.Empty<FleetSpan>() : [FleetSpan.Muted($"   {entry.Date}")],
                ]));

                foreach (var bullet in entry.Bullets)
                {
                    rows.AddRange(Wrap(bullet).Select((line, i) =>
                        FleetRow.Plain($"{(i == 0 ? BulletMark : Continued)}{line}")));
                }
            }
        }

        return rows;
    }

    public static IReadOnlyList<string> Wrap(string text, int width = WrapWidth)
    {
        var lines = new List<string>();
        var line = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            line.Append(line.Length > 0 ? " " : string.Empty).Append(word);
        }

        if (line.Length > 0 || lines.Count == 0)
        {
            lines.Add(line.ToString());
        }

        return lines;
    }
}
