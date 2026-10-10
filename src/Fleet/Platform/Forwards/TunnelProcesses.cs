using System.ComponentModel;
using System.Diagnostics;

namespace Fleet.Platform.Forwards;

public sealed class TunnelProcesses(string program = "ssh") : ITunnelLauncher
{
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

        try
        {
            return Process.Start(start) is { } process
                ? new Running(process)
                : new Ended(127, $"could not run {program}");
        }
        catch (Win32Exception e)
        {
            return new Ended(127, $"could not run {program}: {e.Message}");
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
        private string _errors = string.Empty;

        public Running(Process process)
        {
            _process = process;
            _process.StandardInput.Close();
            _process.OutputDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            Exited = WatchAsync();
        }

        public Task<int> Exited { get; }

        public string Errors => Volatile.Read(ref _errors);

        public void Kill()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
        }

        private async Task<int> WatchAsync()
        {
            using (_process)
            {
                var errors = _process.StandardError.ReadToEndAsync();
                await _process.WaitForExitAsync().ConfigureAwait(false);
                Volatile.Write(ref _errors, await errors.ConfigureAwait(false));
                return _process.ExitCode;
            }
        }
    }
}
