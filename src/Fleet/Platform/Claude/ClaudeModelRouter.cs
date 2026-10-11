using System.Diagnostics;
using Fleet.Ports.Orchestrations;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings.Models;

namespace Fleet.Platform.Claude;

public sealed class ClaudeModelRouter(string executable = AgentHarness.Claude, TimeSpan? timeout = null)
    : IModelRouter
{
    private const string RouterModel = "haiku";

    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(8);

    public TimeSpan Timeout { get; } = timeout ?? DefaultTimeout;

    public async Task<RoleModel?> ChooseAsync(ModelRoutingRequest request, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(ModelRouting.BuildPrompt(request));
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(RouterModel);
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

        using var budget = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
            _ = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);

            return process.ExitCode == 0 ? ModelRouting.Parse(await stdout.ConfigureAwait(false)) : null;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            if (ct.IsCancellationRequested)
            {
                throw;
            }

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
}
