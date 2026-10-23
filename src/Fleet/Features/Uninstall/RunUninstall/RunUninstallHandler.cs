using Fleet.Features.Uninstall.RunUninstall.Models;
using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Enums;

namespace Fleet.Features.Uninstall.RunUninstall;

public sealed class RunUninstallHandler(IUninstallTargets targets)
{
    public const string Question = "Remove all of this? [y/N] ";

    public const string NoTtyRefusal =
        "fleet uninstall: stdin is not a terminal, so it cannot ask; pass --yes (-y) to remove without asking.";

    public UninstallPlan Plan() =>
        new(targets.Discover().OrderBy(i => i.Kind == UninstallKind.Binary).ToList());

    public static IReadOnlyList<string> PlanLines(UninstallPlan plan) =>
        plan.Items.Count == 0
            ? ["fleet uninstall: nothing to remove."]
            : ["fleet uninstall will remove:", .. plan.Items.Select(i => $"  - {i.Label}: {i.Path}")];

    public int Handle(RunUninstallCommand command, Func<bool> confirm, Action<string> write)
    {
        if (!command.Yes && !command.InteractiveInput)
        {
            write(NoTtyRefusal);
            return 1;
        }

        var plan = Plan();

        foreach (var line in PlanLines(plan))
        {
            write(line);
        }

        if (plan.Items.Count == 0)
        {
            return 0;
        }

        if (!command.Yes && !confirm())
        {
            write("fleet uninstall: cancelled; nothing was removed.");
            return 0;
        }

        return Execute(plan, write);
    }

    public int Execute(UninstallPlan plan, Action<string> write)
    {
        var failed = 0;

        foreach (var item in plan.Items)
        {
            try
            {
                targets.Remove(item);
                write($"removed {item.Label}: {item.Path}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed++;
                write($"could not remove {item.Label} ({item.Path}): {e.Message}");
            }
        }

        if (failed > 0)
        {
            write($"fleet uninstall: {failed} item(s) could not be removed.");
            return 1;
        }

        write("fleet uninstall: done.");
        return 0;
    }
}
