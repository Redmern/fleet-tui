using System.Diagnostics;
using Fleet.Ports.Forge;
using Fleet.Ports.Forge.Models;

namespace Fleet.Platform.Forge;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> args,
        string workDir,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (Directory.Exists(workDir))
        {
            psi.WorkingDirectory = workDir;
        }

        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        psi.Environment["NO_COLOR"] = "1";

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
            return new ProcessResult(ProcessResult.NotFound, string.Empty, e.Message);
        }

        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
