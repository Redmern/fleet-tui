using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Uninstall.RunUninstall;
using Fleet.Features.Uninstall.RunUninstall.Models;

namespace Fleet.Cli.Commands;

public static class UninstallCommand
{
    public static int Run(Invocation invocation) =>
        new RunUninstallHandler(Adapters.UninstallTargets()).Handle(
            new RunUninstallCommand(CommandLine.Yes(invocation), !Console.IsInputRedirected),
            Confirm,
            Console.WriteLine);

    private static bool Confirm()
    {
        Console.Write(RunUninstallHandler.Question);
        var answer = Console.ReadLine()?.Trim();

        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
