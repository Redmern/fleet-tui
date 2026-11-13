using Xunit.Sdk;

namespace Fleet.Tests.Support;

public sealed class TimingTests
{
    [Fact]
    public async Task A_latency_check_that_fails_once_passes_on_its_second_try()
    {
        var tries = 0;

        await Timing.RetryOnceAsync(() =>
        {
            tries++;
            Assert.True(tries > 1, "too slow");
            return Task.CompletedTask;
        });

        Assert.Equal(2, tries);
    }

    [Fact]
    public async Task A_latency_check_that_passes_at_once_runs_once()
    {
        var tries = 0;

        await Timing.RetryOnceAsync(() =>
        {
            tries++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, tries);
    }

    [Fact]
    public async Task A_latency_check_that_fails_twice_fails_with_both_reasons()
    {
        var tries = 0;

        var failed = await Assert.ThrowsAnyAsync<XunitException>(() => Timing.RetryOnceAsync(() =>
        {
            tries++;
            Assert.Fail($"slow on try {tries}");
            return Task.CompletedTask;
        }));

        Assert.Equal(2, tries);
        Assert.Contains("slow on try 1", failed.Message, StringComparison.Ordinal);
        Assert.Contains("slow on try 2", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_error_that_is_not_a_failed_check_is_not_retried()
    {
        var tries = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Timing.RetryOnceAsync(() =>
        {
            tries++;
            throw new InvalidOperationException("broken rig");
        }));

        Assert.Equal(1, tries);
    }
}
