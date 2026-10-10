namespace Fleet.Tests.Platform.Mux.Embedded;

// A virtual clock for the simulated link: time only moves when a test calls Advance, so
// latency tests are exact and never sleep. Timers due by the new time fire in due order.
public sealed class SimulatedLinkClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;
        lock (_gate)
        {
            target = _now + by;
        }

        while (true)
        {
            Timer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.Due <= target).MinBy(t => t.Due);
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.Due > _now ? next.Due : _now;
                next.Rearm();
            }

            next.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class Timer(SimulatedLinkClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                clock._timers.Remove(this);
                _period = period;
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    Due = clock._now + dueTime;
                    clock._timers.Add(this);
                }
            }

            return true;
        }

        public void Rearm()
        {
            clock._timers.Remove(this);
            if (_period != Timeout.InfiniteTimeSpan && _period > TimeSpan.Zero)
            {
                Due += _period;
                clock._timers.Add(this);
            }
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock._gate)
            {
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
