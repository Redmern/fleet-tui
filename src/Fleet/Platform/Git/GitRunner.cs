using System.Diagnostics;
using Fleet.Ports.Git;

namespace Fleet.Platform.Git;

public sealed class GitRunner(string executable = "git") : IGitRunner
{
    public async Task<GitResult> RunAsync(
        string workDir,
        IReadOnlyList<string> args,
        string? stdin = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

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
            // git missing from PATH is reported as a result, not an exception:
            // doctor needs to say so rather than crash.
            return new GitResult(127, string.Empty, e.Message);
        }

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        // Both streams must be drained concurrently with the wait, or a command
        // producing more output than the pipe buffer deadlocks.
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return new GitResult(
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }
}
