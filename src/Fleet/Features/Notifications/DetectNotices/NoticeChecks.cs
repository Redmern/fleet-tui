namespace Fleet.Features.Notifications.DetectNotices;

public static class NoticeChecks
{
    public static readonly TimeSpan RetellEvery = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan BaseEvery = TimeSpan.FromMinutes(5);
}