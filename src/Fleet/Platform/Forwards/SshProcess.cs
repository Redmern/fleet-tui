using System.Diagnostics;
using Fleet.Platform.Forwards.Models;

namespace Fleet.Platform.Forwards;

public static class SshProcess
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    public static async Task<SshResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo("ssh")
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

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return new SshResult(127, string.Empty, $"could not run ssh: {e.Message}");
        }

        if (process is null)
        {
            return new SshResult(127, string.Empty, "could not run ssh");
        }

        using (process)
        {
            process.StandardInput.Close();
            using var within = CancellationTokenSource.CreateLinkedTokenSource(ct);
            within.CancelAfter(Patience);
            var output = process.StandardOutput.ReadToEndAsync(within.Token);
            var errors = process.StandardError.ReadToEndAsync(within.Token);

            try
            {
                await process.WaitForExitAsync(within.Token).ConfigureAwait(false);
                return new SshResult(process.ExitCode, await output.ConfigureAwait(false), await errors.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                ct.ThrowIfCancellationRequested();
                return new SshResult(124, string.Empty, $"ssh did not finish within {Patience.TotalSeconds:0}s");
            }
        }
    }
}
