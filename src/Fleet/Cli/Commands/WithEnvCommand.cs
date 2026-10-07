using System.Diagnostics;
using Fleet.Cli.Models;
using Fleet.Shared.Constants;

namespace Fleet.Cli.Commands;

public static class WithEnvCommand
{
    public static int Run(Invocation invocation)
    {
        var arguments = invocation.Arguments ?? [];
        var separator = arguments.ToList().IndexOf("--");
        var command = invocation.Tail ?? [];

        if (separator < 0 || command.Count == 0)
        {
            Console.Error.WriteLine(
                $"fleet {AgentHarness.WithEnvVerb}: usage: fleet {AgentHarness.WithEnvVerb} "
                + "[NAME=value ...] -- <command> [args]");

            return 2;
        }

        Console.CancelKeyPress += (_, e) => e.Cancel = true;

        var psi = new ProcessStartInfo(command[0]) { UseShellExecute = false };

        foreach (var pair in arguments.Take(separator))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);

            if (equals > 0)
            {
                psi.Environment[pair[..equals]] = equals == pair.Length - 1 ? null : pair[(equals + 1)..];
            }
        }

        foreach (var arg in command.Skip(1))
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);

            if (process is null)
            {
                Console.Error.WriteLine($"fleet {AgentHarness.WithEnvVerb}: could not start {command[0]}.");
                return 1;
            }

            process.WaitForExit();

            return process.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Console.Error.WriteLine(
                $"fleet {AgentHarness.WithEnvVerb}: could not start {command[0]}: {e.Message}");
            return 1;
        }
    }
}
