using System.Diagnostics;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;

namespace Fleet.Platform.Git;

public sealed class GitRunner(string executable = "git") : IGitRunner
{
    public static IReadOnlyList<string> Argv(string workDir, IReadOnlyList<string> args) =>
        string.IsNullOrWhiteSpace(workDir) ? [.. args] : ["-C", workDir, .. args];

    public async Task<GitResult> RunAsync(
        string workDir,
        IReadOnlyList<string> args,
        string? stdin = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in Argv(workDir, args))
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
            return new GitResult(127, string.Empty, e.Message);
        }

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return new GitResult(
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }
}
