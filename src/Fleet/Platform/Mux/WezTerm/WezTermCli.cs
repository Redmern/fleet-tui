using System.Diagnostics;
using Fleet.Ports.Mux;

namespace Fleet.Platform.Mux.WezTerm;

/// <summary>Runs `wezterm cli ...` with a bounded timeout.</summary>
public sealed class WezTermCli(string executable = "wezterm")
{
    /// <summary>
    /// WezTerm answers in single-digit milliseconds. Anything slower means the mux
    /// is wedged, and degrading beats hanging a dashboard refresh.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public async Task<string> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("cli");
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new MuxUnavailableException("wezterm is not on PATH", e);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);

        // Drained concurrently with the wait: a command producing more output than
        // the pipe buffer would otherwise deadlock.
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);

        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"wezterm cli {string.Join(' ', args)} timed out");
        }

        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error)
                ? $"exit {process.ExitCode}"
                : error.Trim();

            throw new MuxUnavailableException($"wezterm cli {args[0]}: {message}");
        }

        return output;
    }

    private static void TryKill(Process p)
    {
        try
        {
            p.Kill(entireProcessTree: true);
        }
        catch (Exception e)
            when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }
}
