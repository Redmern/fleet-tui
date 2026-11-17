using System.Diagnostics;
using Fleet.Features.Setup.WriteInclude;
using Fleet.Features.Setup.WriteInclude.Enums;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;

namespace Fleet.Cli.Composition;

public static class IncludeWiring
{
    public const string IncludeOnlyFlag = "--include-only";

    private static readonly TimeSpan NewBinaryLimit = TimeSpan.FromSeconds(30);

    public static string IncludePath => OrchestratorInclude.PathIn(FleetPaths.Config);

    public static Result<IncludeWrite> Write() => IncludeWriter.Write(FleetPaths.Config);

    public static string? WriteHere() => Write() is { Succeeded: false } failed ? failed.Error : null;

    public static void OnDaemonStart(IFleetLog log)
    {
        var outcome = Write();

        if (!outcome.Succeeded)
        {
            log.Write($"fleetd: {outcome.Error}");
        }
        else if (outcome.Value == IncludeWrite.Written)
        {
            log.Write($"fleetd: wrote this build's orchestrator instructions to {IncludePath}");
        }
    }

    public static bool InstalledWrites(string executable) => InstalledWrites(executable, NewBinaryLimit);

    public static bool InstalledWrites(string executable, TimeSpan limit)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("setup");
        start.ArgumentList.Add(IncludeOnlyFlag);

        try
        {
            using var process = Process.Start(start);

            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(limit))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return Task.WaitAll([output, errors], limit) && process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }
}
