using Fleet.Ports.Notifications.Models;

namespace Fleet.Features.Notifications.ShowNotices;

public static class NoticeTabs
{
    public const string AllTab = "All";

    public const string HistoryTab = "History";

    public static IReadOnlyList<string> Names(IEnumerable<string> projects) => [AllTab, .. projects, HistoryTab];

    public static bool ShowsProject(string tab) => tab is AllTab or HistoryTab;

    public static IReadOnlyList<Notice> For(string tab, IReadOnlyDictionary<string, IReadOnlyList<Notice>> all) => tab switch
    {
        AllTab => [.. all.Values.SelectMany(n => n).Where(n => n.IsOpen).OrderByDescending(n => n.Since)],
        HistoryTab => [.. all.Values.SelectMany(n => n).Where(n => !n.IsOpen).OrderByDescending(Ended)],
        _ => [.. all.GetValueOrDefault(tab, []).Where(n => n.IsOpen).OrderByDescending(n => n.Since)],
    };

    public static string Title(string tab, IReadOnlyList<Notice> shown) => $"{tab} ({shown.Count})";

    private static DateTime Ended(Notice notice) => notice.Dismissed ?? notice.Resolved ?? notice.Since;
}