using Fleet.Features.Menu.ShowReleaseNotes.Models;

namespace Fleet.Features.Menu.ShowReleaseNotes;

public static class ReleaseNotes
{
    public const string ResourceName = "RELEASE_NOTES.md";

    public const string LegacyHeading = "# Earlier builds";

    private const string Heading = "## ";

    private const string Bullet = "- ";

    public static string Embedded()
    {
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded in fleet");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<ReleaseEntry> Parse(string markdown)
    {
        var entries = new List<ReleaseEntry>();
        string? version = null;
        string? date = null;
        var legacy = false;
        var bullets = new List<string>();

        void Flush()
        {
            if (version is not null)
            {
                entries.Add(new ReleaseEntry(version, date, [.. bullets], legacy));
            }

            bullets.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ');

            if (line.StartsWith(LegacyHeading, StringComparison.Ordinal))
            {
                Flush();
                version = null;
                legacy = true;
            }
            else if (line.StartsWith(Heading, StringComparison.Ordinal))
            {
                Flush();
                (version, date) = Title(line[Heading.Length..]);
            }
            else if (version is not null && line.StartsWith(Bullet, StringComparison.Ordinal))
            {
                bullets.Add(line[Bullet.Length..].Trim());
            }
            else if (version is not null && bullets.Count > 0 && line.StartsWith("  ", StringComparison.Ordinal))
            {
                bullets[^1] = $"{bullets[^1]} {line.Trim()}";
            }
        }

        Flush();

        return entries;
    }

    public static IReadOnlyList<ReleaseGroup> Group(IReadOnlyList<ReleaseEntry> entries) =>
        [
            .. entries
                .GroupBy(e => (Minor: Minor(e.Version), e.Legacy))
                .Select(g => new ReleaseGroup(g.Key.Minor, [.. g], g.Key.Legacy)),
        ];

    public static string Minor(string version)
    {
        var core = version.TrimStart('v').Split('-', 2)[0];
        var parts = core.Split('.');

        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : core;
    }

    private static (string Version, string? Date) Title(string title)
    {
        var open = title.IndexOf('(', StringComparison.Ordinal);

        return open < 0
            ? (title.Trim(), null)
            : (title[..open].Trim(), title[(open + 1)..].TrimEnd(')').Trim());
    }
}
