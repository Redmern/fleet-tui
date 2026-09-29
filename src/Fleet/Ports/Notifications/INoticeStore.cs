using Fleet.Ports.Notifications.Models;

namespace Fleet.Ports.Notifications;

public interface INoticeStore
{
    IReadOnlyList<Notice> Load(string project);

    void Save(string project, IReadOnlyList<Notice> notices);

    IReadOnlyList<string> Projects();

    NoticeSettings Settings();

    void Save(NoticeSettings settings);
}