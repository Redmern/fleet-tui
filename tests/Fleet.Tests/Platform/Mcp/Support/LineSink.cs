using System.Text;

namespace Fleet.Tests.Platform.Mcp.Support;

// A writer that keeps every finished line and yields after each async write, so two writers that do not share a
// lock mix their text into one line. WaitForLines lets a test wait for output without sleeping.
internal sealed class LineSink : TextWriter
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly StringBuilder _open = new();
    private readonly List<string> _lines = [];
    private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = [];

    public override Encoding Encoding => Encoding.UTF8;

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
        foreach (var value in buffer.ToArray())
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
