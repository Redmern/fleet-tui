using Fleet.Ports.Notifications.Enums;
using Fleet.Shared;

namespace Fleet.Ports.Notifications.Models;

public sealed record Notice(
    string Project,
    NoticeKind Kind,
    string Worktree,
    string Agent,
    string Message,
    DateTime Since,
    DateTime? Resolved = null,
    DateTime? Dismissed = null)
{
    public string Key => $"{Kind}|{PathKey.For(Worktree)}";

    public bool IsOpen => Resolved is null && Dismissed is null;
}