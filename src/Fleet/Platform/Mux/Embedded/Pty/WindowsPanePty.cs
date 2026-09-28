using RoyalTerminal.Terminal;

namespace Fleet.Platform.Mux.Embedded.Pty;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsPanePty : IPanePty
{
    private readonly WindowsPty _pty = new();
    private readonly CancellationTokenSource _stop = new();
    private int _exited;

    public event Action<byte[], int>? Output;

    public event Action<int>? Exited;

    public void Start(
        string program,
        IReadOnlyList<string> args,
        int cols,
        int rows,
        string cwd,
        IReadOnlyDictionary<string, string> env)
    {
        _pty.DataReceived += (buffer, count) => Output?.Invoke(buffer, count);
        _pty.ProcessExited += Exit;

        var environment = new Dictionary<string, string>(env)
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
        };

        var directory = Directory.Exists(cwd) ? cwd : Environment.CurrentDirectory;
        _pty.Start(ResolveProgram(program), cols, rows, directory, environment, [.. args]);
        _ = Task.Run(WatchAsync);
    }

    private async Task WatchAsync()
    {
        System.Diagnostics.Process child;
        try
        {
            child = System.Diagnostics.Process.GetProcessById(_pty.ChildPid);
        }
        catch (ArgumentException)
        {
            Exit(0);
            return;
        }

        using (child)
        {
            try
            {
                await child.WaitForExitAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Exit(child.HasExited ? child.ExitCode : 0);
        }
    }

    private void Exit(int code)
    {
        if (Interlocked.Exchange(ref _exited, 1) == 0)
        {
            Exited?.Invoke(code);
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        _pty.Write(copy, 0, copy.Length);
    }

    public void Resize(int cols, int rows) => _pty.Resize(cols, rows);

    public void Dispose()
    {
        _stop.Cancel();

        try
        {
            _pty.Stop();
        }
        catch (InvalidOperationException)
        {
        }

        _pty.Dispose();
    }

    private static string ResolveProgram(string program)
    {
        if (Path.IsPathRooted(program) || Path.HasExtension(program))
        {
            return program;
        }

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in dirs)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, program + ext.ToLowerInvariant());
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return program;
    }
}
