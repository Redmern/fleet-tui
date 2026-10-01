using Fleet.Ports.Notifications.Models;

namespace Fleet.Features.Notifications.SyncNotices;

public static class NoticeSync
{
    public static readonly TimeSpan KeepHistory = TimeSpan.FromDays(1);

    public static IReadOnlyList<Notice> Apply(IReadOnlyList<Notice> stored, IReadOnlyList<Notice> detected, DateTime now)
    {
        var result = new List<Notice>();
        var live = stored.Where(n => n.Resolved is null).ToDictionary(n => n.Key);
        var seen = new HashSet<string>();

        foreach (var found in detected)
        {
            if (!seen.Add(found.Key))
            {
                continue;
            }

            result.Add(live.TryGetValue(found.Key, out var existing)
                ? existing with { Message = found.Message, Agent = found.Agent }
                : found with { Since = now, Resolved = null, Dismissed = null });
        }

        foreach (var gone in live.Values.Where(n => !seen.Contains(n.Key)))
        {
            result.Add(gone with { Resolved = now });
        }

        result.AddRange(stored.Where(n => n.Resolved is { } resolved && now - resolved < KeepHistory));

        return [.. result.OrderByDescending(n => n.IsOpen).ThenByDescending(n => n.Since)];
    }

    public static IReadOnlyList<Notice> Fresh(IReadOnlyList<Notice> before, IReadOnlyList<Notice> after)
    {
        var wasOpen = before.Where(n => n.Resolved is null).Select(n => n.Key).ToHashSet();
        return [.. after.Where(n => n.IsOpen && !wasOpen.Contains(n.Key))];
    }

    public static string Summary(IReadOnlyList<Notice> fresh) =>
        fresh.Count == 1
            ? $"{fresh[0].Agent}: {fresh[0].Message}"
            : string.Join('\n', fresh.Take(3).Select(n => $"{n.Agent}: {n.Message}")) + (fresh.Count > 3 ? $"\n+{fresh.Count - 3} more" : string.Empty);

    public static IReadOnlyList<Notice> Dismiss(IReadOnlyList<Notice> stored, IReadOnlyCollection<string> keys, DateTime now) =>
        [.. stored.Select(n => n.IsOpen && keys.Contains(n.Key) ? n with { Dismissed = now } : n)];
}
