using Fleet.Features.Mcp.ServeMcp;
using Fleet.Shared.Harness;

namespace Fleet.Tests.Features.Mcp;

public sealed class ApprovalDeadlineTests
{
    [Fact]
    public void A_harness_with_a_long_client_limit_keeps_the_two_minute_default()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(120),
            ApprovalDeadline.For(HarnessRegistry.Claude.ClientToolTimeout));
    }

    [Theory]
    [InlineData(nameof(HarnessRegistry.Codex))]
    [InlineData(nameof(HarnessRegistry.Pi))]
    [InlineData(nameof(HarnessRegistry.Opencode))]
    public void A_sixty_second_harness_gets_five_seconds_less_than_its_limit(string harness)
    {
        var profile = HarnessRegistry.For(harness.ToLowerInvariant());

        Assert.Equal(TimeSpan.FromSeconds(60), profile.ClientToolTimeout);
        Assert.Equal(TimeSpan.FromSeconds(55), ApprovalDeadline.For(profile.ClientToolTimeout));
    }

    [Fact]
    public void A_harness_without_a_known_limit_keeps_the_default()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), ApprovalDeadline.For(null));
    }

    [Fact]
    public void A_tiny_limit_never_leaves_a_zero_wait()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), ApprovalDeadline.For(TimeSpan.FromSeconds(3)));
    }
}
