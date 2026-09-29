using System.Globalization;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ports.Notifications.Models;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Notifications.ShowNotices;

public static class NoticeRows
{
    public const string EmptyHint = "(no notifications)";

    public static IReadOnlyList<FleetRow> For(IReadOnlyList<Notice> notices, bool withProject, DateTime now)
    {
        if (notices.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint)];
        }

        var widest = withProject ? notices.Max(n => n.Project.Length) : 0;

        return [.. notices.Select(n =>
        {
            var (mark, tone) = Look(n.Kind);
            var open = n.IsOpen;
            var text = open ? FleetTones.Normal : FleetTones.Muted;

            List<FleetSpan> spans = [new($" {mark} ", open ? tone : FleetTones.Muted)];
            if (withProject)
            {
                spans.Add(new($"{n.Project.PadRight(widest)}  ", open ? FleetTones.Key : FleetTones.Muted));
            }

            spans.Add(new(n.Agent, text));
            spans.Add(new($"  {n.Message}", FleetTones.Muted));

            var state = n.Dismissed is not null ? "dismissed" : n.Resolved is not null ? "resolved" : Age(now - n.Since);
            return new FleetRow(spans, [new($"{state} ", FleetTones.Muted)]);
        })];
    }

    public static (string Mark, string Tone) Look(NoticeKind kind) => kind switch
    {
        NoticeKind.NeedsInput => ("?", FleetTones.Warn),
        NoticeKind.Permission => ("!", FleetTones.Warn),
        NoticeKind.Done => ("✓", FleetTones.Good),
        NoticeKind.Failed => ("✗", FleetTones.Bad),
        NoticeKind.Stalled => ("…", FleetTones.Warn),
        _ => ("↕", FleetTones.Bad),
    };

    public static string Age(TimeSpan age) =>
        age.TotalMinutes < 1 ? "now"
        : age.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes}m")
        : age.TotalDays < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours}h")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalDays}d");
}
