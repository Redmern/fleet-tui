namespace Fleet.Tests.Platform.Mcp.Support;

// A clock that only moves when a test advances it. Advance fires every timer that falls due on the way, in time
// order and on the calling thread, and a periodic timer fires once per period crossed.
internal sealed class ManualClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public int ActiveTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _ticks;
        }
    }

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(GetTimestamp());

    public void Advance(TimeSpan by)
    {
        long target;

        lock (_gate)
        {
            target = _ticks + by.Ticks;
        }

        while (true)
        {
            ManualTimer? due;

            lock (_gate)
            {
                due = _timers.Where(t => t.DueAt <= target).MinBy(t => t.DueAt);

                if (due is null)
                {
                    _ticks = target;

                    return;
                }

                _ticks = due.DueAt;

                if (due.Period > TimeSpan.Zero)
                {
                    due.DueAt += due.Period.Ticks;
                }
                else
                {
                    _timers.Remove(due);
                }
            }

            due.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);

        timer.Change(dueTime, period);

        return timer;
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public long DueAt { get; set; }

        public TimeSpan Period { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                clock._timers.Remove(this);

                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    return true;
                }

                DueAt = clock._ticks + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                clock._timers.Add(this);

                return true;
            }
        }

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
