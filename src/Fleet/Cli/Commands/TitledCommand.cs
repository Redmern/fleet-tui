using System.Diagnostics;
using Fleet.Cli.Models;
using Fleet.Shared.Constants;

namespace Fleet.Cli.Commands;

public static class TitledCommand
{
    private static readonly string Osc = (char)27 + "]2;";

    private static readonly string Bel = ((char)7).ToString();

    public static int Run(Invocation invocation)
    {
        var command = invocation.Tail ?? [];

        if (invocation.Title is null || command.Count == 0)
        {
            Console.Error.WriteLine(
                $"fleet {AgentHarness.TitledVerb}: usage: fleet {AgentHarness.TitledVerb} "
                + $"{AgentHarness.TitleFlag} <title> -- <command> [args]");

            return 2;
        }

        Console.Out.Write(Osc + invocation.Title + Bel);
        Console.Out.Flush();

        Console.CancelKeyPress += (_, e) => e.Cancel = true;

        var psi = new ProcessStartInfo(command[0]) { UseShellExecute = false };

        foreach (var arg in command.Skip(1))
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);

            if (process is null)
            {
                Console.Error.WriteLine($"fleet {AgentHarness.TitledVerb}: could not start {command[0]}.");
                return 1;
            }

            process.WaitForExit();

            return process.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Console.Error.WriteLine(
                $"fleet {AgentHarness.TitledVerb}: could not start {command[0]}: {e.Message}");
            return 1;
        }
    }
}
