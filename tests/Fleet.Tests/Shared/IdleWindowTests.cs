using Fleet.Shared.Harness;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Status;

namespace Fleet.Tests.Shared;

public sealed class IdleWindowTests
{
    private static readonly HarnessProfile Slow = HarnessRegistry.Opencode with { IdleWindow = TimeSpan.FromSeconds(300) };

    private static SettingsConfig Set(int? seconds) => SettingsConfig.Default with { IdleWindowSeconds = seconds };

    [Fact]
    public void The_default_is_two_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), IdleWindow.Default);
        Assert.Equal(IdleWindow.Default, IdleWindow.For(SettingsConfig.Default, HarnessRegistry.Opencode));
        Assert.Equal(IdleWindow.Default, IdleWindow.For(SettingsConfig.Default, null));
    }

    [Fact]
    public void A_harness_profile_overrides_the_default() =>
        Assert.Equal(TimeSpan.FromSeconds(300), IdleWindow.For(SettingsConfig.Default, Slow));

    [Fact]
    public void The_users_setting_wins_over_the_profile_and_the_default()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), IdleWindow.For(Set(45), Slow));
        Assert.Equal(TimeSpan.FromSeconds(45), IdleWindow.For(Set(45), HarnessRegistry.Opencode));
        Assert.Equal(TimeSpan.FromSeconds(45), IdleWindow.For(Set(45), null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_setting_that_is_no_duration_counts_as_not_set(int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(300), IdleWindow.For(Set(seconds), Slow));

    // No capture showed a harness that needs its own window, so every shipped profile takes the default.
    [Fact]
    public void No_shipped_profile_changes_the_default() =>
        Assert.All(HarnessRegistry.Profiles, p => Assert.Null(p.IdleWindow));
}
