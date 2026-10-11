using System.ComponentModel;
using System.Diagnostics;

namespace Fleet.Platform.Forwards;

public sealed class TunnelProcesses(string program = "ssh") : ITunnelLauncher, IDisposable
{
    public const int KeptErrorLines = 20;

    private readonly Lock _gate = new();
    private readonly HashSet<Running> _running = [];
    private bool _disposed;

    public void Dispose()
    {
        List<Running> running;
        lock (_gate)
        {
            _disposed = true;
            running = [.. _running];
            _running.Clear();
        }

        foreach (var tunnel in running)
        {
            tunnel.Kill();
        }
    }

    public ITunnel Start(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return new Ended(143, "fleet is stopping");
            }

            Process? process;
            try
            {
                process = Process.Start(start);
            }
            catch (Win32Exception e)
            {
                return new Ended(127, $"could not run {program}: {e.Message}");
            }

            if (process is null)
            {
                return new Ended(127, $"could not run {program}");
            }

            KillOnCloseJob.Hold(process.Handle);
            var running = new Running(process);
            _running.Add(running);
            _ = running.Exited.ContinueWith(_ => Forget(running), TaskScheduler.Default);
            return running;
        }
    }

    private void Forget(Running running)
    {
        lock (_gate)
        {
            _running.Remove(running);
        }
    }

    private sealed class Ended(int exit, string errors) : ITunnel
    {
        public Task<int> Exited { get; } = Task.FromResult(exit);

        public string Errors => errors;

        public void Kill()
        {
        }
    }

    private sealed class Running : ITunnel
    {
        private readonly Process _process;
        private readonly Queue<string> _errors = new();

        public Running(Process process)
        {
            _process = process;
            _process.StandardInput.Close();
            _process.OutputDataReceived += (_, _) => { };
            _process.ErrorDataReceived += (_, line) => Keep(line.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            Exited = WatchAsync();
        }

        public Task<int> Exited { get; }

        public string Errors
        {
            get
            {
                lock (_errors)
                {
                    return string.Join('\n', _errors);
                }
            }
        }

        public void Kill()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException or ObjectDisposedException)
            {
            }
        }

        private async Task<int> WatchAsync()
        {
            using (_process)
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
                return _process.ExitCode;
            }
        }

        private void Keep(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_errors)
            {
                _errors.Enqueue(line);
                while (_errors.Count > KeptErrorLines)
                {
                    _errors.Dequeue();
                }
            }
        }
    }
}
