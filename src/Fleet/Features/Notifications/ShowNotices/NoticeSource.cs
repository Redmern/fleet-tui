using Fleet.Ports.Notifications.Models;

namespace Fleet.Features.Notifications.ShowNotices;

public sealed record NoticeSource(IReadOnlyList<string> Projects, Func<IReadOnlyList<Notice>> Load);