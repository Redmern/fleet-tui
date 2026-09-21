using System.Diagnostics;
using Fleet.Ports.Orchestrations;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Claude;

public sealed class ClaudeSlugNamer(string executable = AgentHarness.Claude, TimeSpan? timeout = null)
    : ISlugNamer
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(8);

    public static string BuildPrompt(string prompt) =>
        "Reply with ONLY a short kebab-case slug (2 to 5 words, lowercase ascii letters, digits and "
        + "hyphens, no punctuation, no quotes, no explanation) that names this task for use as a "
        + "folder name. Task:\n\n" + prompt;

    public async Task<string?> NameAsync(string prompt, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(BuildPrompt(prompt));
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("text");

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        using var budget = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderr = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);

            return process.ExitCode == 0 ? FirstLine(await stdout.ConfigureAwait(false)) : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string? FirstLine(string stdout)
    {
        var line = stdout
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        return string.IsNullOrEmpty(line) ? null : line;
    }
}
