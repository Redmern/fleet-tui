namespace Fleet.Tests.EndToEnd.MixedTeam;

// A fake clock that never sleeps: a delay taken on it moves the time forward by its length and completes at once.
// Time starts at the real time and only moves forward, so stamps written with it stay comparable with file times.
// Skipped fires after every move, which is how a fake harness gets its turn while fleet waits for it.
// Delays lists every delay taken on it, in order, so a test can tell which waits ran on this clock.
internal sealed class SkippingClock : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = System.GetUtcNow();
    private readonly List<TimeSpan> _delays = [];

    public event Action? Skipped;

    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_gate)
            {
                return [.. _delays];
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
        }

        Skipped?.Invoke();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_gate)
            {
                _delays.Add(dueTime);
            }

            Advance(dueTime);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new Spent();
    }

    private sealed class Spent : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
