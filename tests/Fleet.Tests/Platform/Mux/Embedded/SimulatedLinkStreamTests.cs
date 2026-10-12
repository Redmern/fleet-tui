using System.Text;
using Fleet.Platform.Mux.Embedded.Daemon;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class SimulatedLinkStreamTests : IAsyncLifetime
{
    private static readonly TimeSpan OneWay = TimeSpan.FromMilliseconds(100);

    private readonly SimulatedLinkClock _clock = new();
    private Endpoint.IListener _listener = null!;
    private Stream _far = null!;
    private SimulatedLinkStream _near = null!;

    public async Task InitializeAsync()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        var endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        _listener = endpoint.Listen();
        var accepting = _listener.AcceptAsync(CancellationToken.None);
        _near = new SimulatedLinkStream(await endpoint.ConnectAsync(TimeSpan.FromSeconds(5)), OneWay, _clock);
        _far = await accepting;
    }

    public async Task DisposeAsync()
    {
        await _near.DisposeAsync();
        await _far.DisposeAsync();
        _listener.Dispose();
    }

    private static async Task<string> ReadAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            read += await stream.ReadAsync(buffer.AsMemory(read));
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task<bool> StillWaiting(Task task)
    {
        await Task.WhenAny(task, Task.Delay(100));
        return !task.IsCompleted;
    }

    [Fact]
    public async Task What_the_near_end_writes_reaches_the_far_end_one_way_delay_later()
    {
        await _near.WriteAsync("ping"u8.ToArray());
        await _near.FlushAsync();
        var arriving = ReadAsync(_far, 4);

        _clock.Advance(OneWay - TimeSpan.FromMilliseconds(1));
        Assert.True(await StillWaiting(arriving));

        await StepUntil(arriving);
        Assert.Equal("ping", await arriving);
    }

    [Fact]
    public async Task What_the_far_end_writes_reaches_the_near_end_one_way_delay_later()
    {
        await _far.WriteAsync("pong"u8.ToArray());
        await _far.FlushAsync();
        await Eventually(() => _near.InFlight > 0);
        var arriving = ReadAsync(_near, 4);

        _clock.Advance(OneWay - TimeSpan.FromMilliseconds(1));
        Assert.True(await StillWaiting(arriving));

        await StepUntil(arriving);
        Assert.Equal("pong", await arriving);
        Assert.Equal(0, _near.InFlight);
    }

    [Fact]
    public async Task Writes_keep_their_order_across_the_link()
    {
        var start = _clock.GetUtcNow();
        await _near.WriteAsync("one-"u8.ToArray());
        _clock.Advance(TimeSpan.FromMilliseconds(30));
        await _near.WriteAsync("two"u8.ToArray());
        var arriving = ReadAsync(_far, 7);

        _clock.Advance(OneWay - TimeSpan.FromMilliseconds(30));
        Assert.True(await StillWaiting(arriving));

        await StepUntil(arriving);
        Assert.Equal("one-two", await arriving);
        Assert.True(_clock.GetUtcNow() - start >= OneWay + TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public async Task Bytes_a_coarse_timer_delivers_late_count_as_held_past_due()
    {
        var coarse = new CoarseTimers(_clock, TimeSpan.FromMilliseconds(15));
        await using var near = new SimulatedLinkStream(new MemoryStream("pong"u8.ToArray()), OneWay, coarse);
        await Eventually(() => near.InFlight > 0);
        var start = _clock.GetUtcNow();
        var arriving = ReadAsync(near, 4);

        await StepUntil(arriving);
        Assert.Equal("pong", await arriving);
        Assert.True(_clock.GetUtcNow() - start >= OneWay + TimeSpan.FromMilliseconds(15));
        // At least the overshoot; stepping the clock can add a few ms after the timer fires.
        Assert.True(near.HeldPastDue >= TimeSpan.FromMilliseconds(15), $"{near.HeldPastDue}");
    }

    [Fact]
    public async Task A_reader_that_comes_late_is_not_held_past_due()
    {
        await _far.WriteAsync("pong"u8.ToArray());
        await _far.FlushAsync();
        await Eventually(() => _near.InFlight > 0);

        _clock.Advance(OneWay * 3);
        var read = await ReadAsync(_near, 4).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("pong", read);
        Assert.Equal(TimeSpan.Zero, _near.HeldPastDue);
    }

    [Fact]
    public async Task A_closed_far_end_ends_the_near_reads()
    {
        await _far.DisposeAsync();

        var read = await _near.ReadAsync(new byte[8]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, read);
    }

    private async Task StepUntil(Task task)
    {
        for (var step = 0; step < 1000 && !task.IsCompleted; step++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(1));
            await Task.WhenAny(task, Task.Delay(5));
        }

        Assert.True(task.IsCompleted);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    // Timers that fire a fixed overshoot after their due time, like the ~15.6 ms OS timer
    // tick on Windows.
    private sealed class CoarseTimers(TimeProvider clock, TimeSpan overshoot) : TimeProvider
    {
        public override long TimestampFrequency => clock.TimestampFrequency;

        public override long GetTimestamp() => clock.GetTimestamp();

        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            clock.CreateTimer(callback, state, dueTime == Timeout.InfiniteTimeSpan ? dueTime : dueTime + overshoot, period);
    }
}
