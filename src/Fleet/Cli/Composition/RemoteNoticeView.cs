using Fleet.Features.Notifications.ShowNotices;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ports.Notifications.Models;

namespace Fleet.Cli.Composition;

public sealed class RemoteNoticeView(IReadOnlyDictionary<string, (string Host, string Project)> labels, Func<IReadOnlyList<NoticeDto>> fetch)
{
    private readonly Dictionary<(string Label, string Key), string> _remoteKeys = [];
    private readonly Lock _gate = new();

    public static string Label(string project, string machine) => $"{project} @{machine}";

    public NoticeSource Source => new([.. labels.Keys], Load);

    public (string Host, string Project)? Locate(Notice notice) =>
        labels.TryGetValue(notice.Project, out var where) ? where : null;

    public bool Dismiss(string label, IReadOnlyList<string> keys)
    {
        if (!labels.TryGetValue(label, out var where))
        {
            return false;
        }

        List<string> remote;
        lock (_gate)
        {
            remote = [.. keys.Select(k => _remoteKeys.GetValueOrDefault((label, k), k))];
        }

        EmbeddedWiring.DismissRemote(where.Host, where.Project, remote);
        return true;
    }

    public IReadOnlyList<Notice> Load()
    {
        var notices = new List<Notice>();

        foreach (var dto in fetch())
        {
            var label = labels.FirstOrDefault(l => string.Equals(l.Value.Host, dto.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(l.Value.Project, dto.Project, StringComparison.OrdinalIgnoreCase)).Key;

            if (label is null || !Enum.TryParse<NoticeKind>(dto.Kind, out var kind))
            {
                continue;
            }

            var notice = new Notice(label, kind, dto.Worktree, dto.Agent, dto.Message, dto.Since, dto.Resolved, dto.Dismissed);
            lock (_gate)
            {
                _remoteKeys[(label, notice.Key)] = dto.Key;
            }

            notices.Add(notice);
        }

        return notices;
    }
}