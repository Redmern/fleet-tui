using System.Text;

namespace Fleet.Tests.Platform.Mcp.Support;

// A writer that keeps every finished line and yields after each async write, so two writers that do not share a
// lock mix their text into one line. WaitForLines lets a test wait for output without sleeping. It writes a message
// in two halves and honours the token between them, like a buffered stream writer, and HoldNextWriteUntil stops the
// next message between its halves so a test can act while the write lock is held.
internal sealed class LineSink : TextWriter
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly StringBuilder _open = new();
    private readonly List<string> _lines = [];
    private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = [];
    private readonly TaskCompletionSource _holding = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _hold;

    public override Encoding Encoding => Encoding.UTF8;

    public Task Holding => _holding.Task;

    public void HoldNextWriteUntil(Task released) => _hold = released;

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    public async Task<IReadOnlyList<string>> WaitForLines(int count)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            if (_lines.Count >= count)
            {
                return [.. _lines];
            }

            _waiters.Add((count, reached));
        }

        await reached.Task.WaitAsync(Patience);

        return Lines;
    }

    public override void Write(char value)
    {
        lock (_gate)
        {
            if (value != '\n')
            {
                _open.Append(value);

                return;
            }

            _lines.Add(_open.ToString());
            _open.Clear();

            foreach (var waiter in _waiters.Where(w => w.Count <= _lines.Count).ToList())
            {
                _waiters.Remove(waiter);
                waiter.Reached.SetResult();
            }
        }
    }

    public override async Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        var text = buffer.ToArray();
        var half = text.Length / 2;

        foreach (var value in text[..half])
        {
            Write(value);
        }

        if (Interlocked.Exchange(ref _hold, null) is { } hold)
        {
            _holding.SetResult();

            await hold.WaitAsync(Patience, CancellationToken.None);
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var value in text[half..])
        {
            Write(value);
        }

        await Task.Yield();
    }

    public override async Task WriteAsync(char value)
    {
        Write(value);

        await Task.Yield();
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
