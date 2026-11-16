using System.Text.RegularExpressions;
using Fleet.Shared.Harness;
using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Status;

public sealed record PaneIdleRule(TimeSpan Window, IReadOnlyList<Regex> VolatilePatterns)
{
    public static PaneIdleRule For(SettingsConfig settings, HarnessProfile? profile) =>
        new(IdleWindow.For(settings, profile), profile?.VolatilePatterns ?? []);

    public static PaneIdleRule For(SettingsConfig settings, string runningHarness) =>
        For(settings, HarnessRegistry.Find(runningHarness));

    public static string Settled(string runningHarness, string paneText) =>
        PaneSettled.Text(paneText, HarnessRegistry.Find(runningHarness)?.VolatilePatterns);

    public bool Quiet(string folder, string paneText, DateTime now) =>
        PaneQuiet.Quiet(PaneQuiet.Observe(folder, paneText, now, VolatilePatterns), now, Window);
}
