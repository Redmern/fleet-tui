using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Uninstall.RunUninstall;
using Fleet.Features.Uninstall.RunUninstall.Models;

namespace Fleet.Cli.Commands;

public static class UninstallCommand
{
    public static int Run(Invocation invocation)
    {
        var interactive = !Console.IsInputRedirected;

        return new RunUninstallHandler(
                Adapters.UninstallTargets(),
                Adapters.DependencyManifest(),
                Adapters.PackageRemover(interactive))
            .Handle(
                new RunUninstallCommand(CommandLine.Yes(invocation), interactive, CommandLine.RemoveDeps(invocation)),
                () => Ask(RunUninstallHandler.Question),
                Console.WriteLine,
                Ask);
    }

    private static bool Ask(string question)
    {
        Console.Write(question);
        var answer = Console.ReadLine()?.Trim();

        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
