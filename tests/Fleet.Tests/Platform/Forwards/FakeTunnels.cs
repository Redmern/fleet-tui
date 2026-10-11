using Fleet.Platform.Forwards;
using Fleet.Ports.Forwards;

namespace Fleet.Tests.Platform.Forwards;

public sealed class FakeTunnels : ITunnelLauncher, IListenerProbe, IDisposable
{
    private readonly List<FakeTunnel> _started = [];

    // Decides what a freshly started tunnel does: null keeps it running.
    public Func<IReadOnlyList<string>, (int Exit, string Errors)?> Exits { get; set; } = _ => null;

    public IReadOnlyList<FakeTunnel> Started
    {
        get
        {
            lock (_started)
            {
                return [.. _started];
            }
        }
    }

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;

    public IEnumerable<string> Specs => Started.Select(t => t.Arguments[t.Arguments.ToList().IndexOf("-L") + 1]);

    public ITunnel Start(IReadOnlyList<string> arguments)
    {
        var tunnel = new FakeTunnel(arguments);
        if (Exits(arguments) is { } exit)
        {
            tunnel.Exit(exit.Exit, exit.Errors);
        }

        lock (_started)
        {
            _started.Add(tunnel);
        }

        return tunnel;
    }

    // Holds every readiness probe until it completes, to catch a forward mid-start.
    public Task Up { get; set; } = Task.CompletedTask;

    public async Task<bool> ListeningAsync(int port, CancellationToken ct = default)
    {
        await Up.WaitAsync(ct);
        return Started.Any(t => !t.Exited.IsCompleted && t.Local == port);
    }
}

public sealed class FakeTunnel(IReadOnlyList<string> arguments) : ITunnel
{
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> Arguments => arguments;

    public int Local => int.Parse(arguments[arguments.ToList().IndexOf("-L") + 1].Split(':')[1]);

    public bool Killed { get; private set; }

    public Task<int> Exited => _exit.Task;

    public string Errors { get; private set; } = string.Empty;

    public void Exit(int code, string errors)
    {
        Errors = errors;
        _exit.TrySetResult(code);
    }

    public void Kill()
    {
        Killed = true;
        _exit.TrySetResult(-1);
    }
}
