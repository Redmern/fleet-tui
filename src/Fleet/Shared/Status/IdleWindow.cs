using Fleet.Shared.Harness;
using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Status;

public static class IdleWindow
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(120);

    public static TimeSpan For(SettingsConfig settings, HarnessProfile? profile) =>
        settings.IdleWindowSeconds is > 0 and var seconds
            ? TimeSpan.FromSeconds(seconds)
            : profile?.IdleWindow ?? Default;
}
