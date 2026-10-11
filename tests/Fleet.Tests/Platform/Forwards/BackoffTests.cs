using Fleet.Platform.Forwards;

namespace Fleet.Tests.Platform.Forwards;

public sealed class BackoffTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(4, 40)]
    [InlineData(5, 60)]
    [InlineData(40, 60)]
    public void The_wait_doubles_per_failure_up_to_the_cap(int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), Backoff.After(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), failures));

    [Fact]
    public void Doubling_stops_after_six_steps_even_below_the_cap() =>
        Assert.Equal(
            TimeSpan.FromMilliseconds(6400),
            Backoff.After(TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1), 20));
}
