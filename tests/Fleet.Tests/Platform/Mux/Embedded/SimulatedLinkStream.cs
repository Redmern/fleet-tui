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

    // A real timer only fires on the OS tick: ~15.6 ms on Windows, so a 50 ms delay lands
    // anywhere up to ~65 ms and each hop of the link gets longer than its nominal latency.
    // On wall time, sleep to within one tick of the due time and yield-spin the rest so the
    // link adds its one-way latency and nothing more. Virtual clocks keep the plain delay.
    private static readonly TimeSpan TimerTick = TimeSpan.FromMilliseconds(20);

    private async Task UntilAsync(long due, CancellationToken ct)
    {
        var wait = _clock.GetElapsedTime(_clock.GetTimestamp(), due);
        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        if (_clock != TimeProvider.System)
        {
            await Task.Delay(wait, _clock, ct);
            return;
        }

        if (wait > TimerTick)
        {
            await Task.Delay(wait - TimerTick, ct);
        }

        while (_clock.GetTimestamp() < due)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
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
                Interlocked.Increment(ref _inFlight);
                _down.Writer.TryWrite((Due, buffer[..read]));
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
        Interlocked.Increment(ref _inFlight);
        _up.Writer.TryWrite((Due, buffer.ToArray()));
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
}
