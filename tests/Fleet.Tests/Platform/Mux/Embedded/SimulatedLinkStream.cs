using System.Threading.Channels;

namespace Fleet.Tests.Platform.Mux.Embedded;

// The near end of a simulated ssh hop: wraps the stream to a (fake) remote fleetd and delays
// every byte by a one-way latency in each direction, keeping order. Time comes from a
// TimeProvider, so a SimulatedLinkClock gives exact virtual-time tests and TimeProvider.System
// gives a real-latency link for end-to-end daemon tests. Reuse it to prove menu-speed gains.
public sealed class SimulatedLinkStream : Stream
{
    private readonly Stream _far;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<(long Due, byte[] Bytes)> _up = Channel.CreateUnbounded<(long, byte[])>();
    private readonly Channel<(long Due, byte[] Bytes)> _down = Channel.CreateUnbounded<(long, byte[])>();
    private readonly Task _upPump;
    private readonly Task _downPump;
    private byte[] _leftover = [];
    private int _leftoverAt;
    private int _inFlight;
    private readonly Lock _held = new();
    private readonly List<(long From, long To)> _heldSpans = [];
    private long _heldUntil;

    public SimulatedLinkStream(Stream far, TimeSpan oneWay, TimeProvider? clock = null)
    {
        _far = far;
        OneWay = oneWay;
        _clock = clock ?? TimeProvider.System;
        _upPump = Task.Run(PumpUpAsync);
        _downPump = Task.Run(PumpDownAsync);
    }

    public TimeSpan OneWay { get; }

    public int InFlight => Volatile.Read(ref _inFlight);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    private long Due => _clock.GetTimestamp() + (long)(OneWay.TotalSeconds * _clock.TimestampFrequency);

    // Time the link held bytes past their due while the other end was already waiting for
    // them: a real timer fires on the OS tick (~15.6 ms on Windows) and its continuation then
    // waits for a thread-pool thread, so a busy runner stretches each hop. A latency test
    // subtracts this to measure fleet rather than the runner. A reader that comes late itself
    // is not held, so time fleet takes to get to its reads still counts against it.
    public TimeSpan HeldPastDue => HeldBetween(long.MinValue, long.MaxValue);

    // The held time inside [from, to] (clock timestamps), so a test credits only what fell
    // within the interval it measured.
    public TimeSpan HeldBetween(long from, long to)
    {
        lock (_held)
        {
            var ticks = _heldSpans.Sum(span => Math.Max(0, Math.Min(span.To, to) - Math.Max(span.From, from)));
            return _clock.GetElapsedTime(0, ticks);
        }
    }

    // Wait with a plain timer, never by spinning: a yield-spin floods the thread pool with
    // work items and can stall it for most of a second on a 2-core runner, which stretched
    // the link by hundreds of milliseconds instead of the few a coarse timer costs.
    private async Task UntilAsync(long due, CancellationToken ct)
    {
        var entered = _clock.GetTimestamp();
        var wait = _clock.GetElapsedTime(entered, due);
        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(wait, _clock, ct);
        Hold(due, _clock.GetTimestamp());
    }

    // Both directions add to one total, so two hops held at the same moment count once.
    private void Hold(long from, long to)
    {
        lock (_held)
        {
            from = Math.Max(from, _heldUntil);
            if (to > from)
            {
                _heldSpans.Add((from, to));
                _heldUntil = to;
            }
        }
    }

    private async Task PumpUpAsync()
    {
        try
        {
            await foreach (var (due, bytes) in _up.Reader.ReadAllAsync(_stop.Token))
            {
                await UntilAsync(due, _stop.Token);
                await _far.WriteAsync(bytes, _stop.Token);
                await _far.FlushAsync(_stop.Token);
                Interlocked.Decrement(ref _inFlight);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private async Task PumpDownAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            int read;
            while ((read = await _far.ReadAsync(buffer, _stop.Token)) > 0)
            {
                var due = Due;
                Interlocked.Increment(ref _inFlight);
                _down.Writer.TryWrite((due, buffer[..read]));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            _down.Writer.TryComplete();
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_leftoverAt >= _leftover.Length)
        {
            if (!await _down.Reader.WaitToReadAsync(cancellationToken) || !_down.Reader.TryRead(out var next))
            {
                return 0;
            }

            await UntilAsync(next.Due, cancellationToken);
            Interlocked.Decrement(ref _inFlight);
            (_leftover, _leftoverAt) = (next.Bytes, 0);
        }

        var n = Math.Min(buffer.Length, _leftover.Length - _leftoverAt);
        _leftover.AsMemory(_leftoverAt, n).CopyTo(buffer);
        _leftoverAt += n;
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var due = Due;
        Interlocked.Increment(ref _inFlight);
        _up.Writer.TryWrite((due, buffer.ToArray()));
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stop.Cancel();
            _up.Writer.TryComplete();
            _far.Dispose();
            Task.WaitAll([_upPump, _downPump], TimeSpan.FromSeconds(5));
            _stop.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _up.Writer.TryComplete();
        await _far.DisposeAsync();
        await Task.WhenAny(Task.WhenAll(_upPump, _downPump), Task.Delay(TimeSpan.FromSeconds(5)));
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }
}
