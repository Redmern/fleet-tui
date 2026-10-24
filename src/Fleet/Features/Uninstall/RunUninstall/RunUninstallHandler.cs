using Fleet.Features.Uninstall.RunUninstall.Models;
using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Features.Uninstall.RunUninstall;

public sealed class RunUninstallHandler(
    IUninstallTargets targets,
    IDependencyManifest manifest,
    IPackageRemover remover)
{
    public RunUninstallHandler(IUninstallTargets targets)
        : this(targets, NoDependencies.Instance, NoDependencies.Instance)
    {
    }

    public const string Question = "Remove all of this? [y/N] ";

    public const string NoTtyRefusal =
        "fleet uninstall: stdin is not a terminal, so it cannot ask; pass --yes (-y) to remove without asking.";

    public static readonly IReadOnlyList<string> RemainsNote =
    [
        "fleet uninstall: left in place: your repositories and their agent worktrees,",
        "  per-project fleet files (.mcp.json, Claude hooks and skills, .fleet) and ~/.claude* entries.",
    ];

    public UninstallPlan Plan() =>
        new(targets.Discover().OrderBy(i => i.Kind == UninstallKind.Binary).ToList())
        {
            Refused = targets.Refusals(),
            Dependencies = manifest.Read(),
        };

    public static IReadOnlyList<string> PlanLines(UninstallPlan plan, RunUninstallCommand? command = null) =>
        [.. plan.Refused.Select(r => $"fleet uninstall: {r}"), .. ItemLines(plan), .. DependencyLines(plan, command)];

    public static string DependencyQuestion(InstalledDependency dependency) =>
        $"Also remove {dependency.ToolName} ({dependency.Package} via {dependency.ManagerName})? [y/N] ";

    private static IReadOnlyList<string> DependencyLines(UninstallPlan plan, RunUninstallCommand? command)
    {
        if (plan.Dependencies.Count == 0)
        {
            return [];
        }

        var mode = command switch
        {
            { RemoveDeps: true } => "fleet uninstall: --remove-deps removes these too, without asking about each.",
            { Yes: true } => "fleet uninstall: --yes alone keeps these; add --remove-deps to remove them too.",
            _ => "fleet uninstall: after you confirm, it asks about each of these separately.",
        };

        return
        [
            "installed by fleet's installer (separate from fleet, kept unless you choose to remove them):",
            .. plan.Dependencies.Select(DependencyLine),
            mode,
        ];
    }

    private static string DependencyLine(InstalledDependency dependency) =>
        $"  - {dependency.ToolName}: {dependency.Package} via {dependency.ManagerName}{(dependency.Sudo ? " (sudo)" : "")}";

    private static IReadOnlyList<string> ItemLines(UninstallPlan plan) =>
        plan.Items.Count == 0
            ? ["fleet uninstall: nothing to remove."]
            : ["fleet uninstall will remove:", .. plan.Items.Select(i => $"  - {i.Label}: {i.Path}")];

    public int Handle(
        RunUninstallCommand command,
        Func<bool> confirm,
        Action<string> write,
        Func<string, bool>? confirmDependency = null)
    {
        if (!command.Yes && !command.InteractiveInput)
        {
            write(NoTtyRefusal);
            return 1;
        }

        var plan = Plan();

        foreach (var line in PlanLines(plan, command))
        {
            write(line);
        }

        if (plan.Items.Count == 0 && plan.Dependencies.Count == 0)
        {
            return 0;
        }

        if (!command.Yes && !confirm())
        {
            write("fleet uninstall: cancelled; nothing was removed.");
            return 0;
        }

        return Execute(plan, write, ChooseDependencies(plan, command, confirmDependency ?? (_ => false)));
    }

    private static IReadOnlyList<InstalledDependency> ChooseDependencies(
        UninstallPlan plan, RunUninstallCommand command, Func<string, bool> confirmDependency)
    {
        if (command.RemoveDeps)
        {
            return plan.Dependencies;
        }

        return command.Yes ? [] : [.. plan.Dependencies.Where(d => confirmDependency(DependencyQuestion(d)))];
    }

    public int Execute(UninstallPlan plan, Action<string> write, IReadOnlyList<InstalledDependency>? dependencies = null)
    {
        var chosen = dependencies ?? [];
        var failed = 0;

        foreach (var dependency in chosen)
        {
            var removal = remover.Remove(dependency);

            if (removal.Succeeded)
            {
                write($"removed {dependency.ToolName} ({dependency.ManagerName} {dependency.Package})");
                continue;
            }

            failed++;
            write($"fleet uninstall: could not remove {dependency.ToolName} ({dependency.ManagerName}): {removal.Message}");
        }

        foreach (var item in plan.Items)
        {
            try
            {
                targets.Remove(item);
                write($"removed {item.Label}: {item.Path}");
            }
            catch (Exception e)
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

        var kept = plan.Dependencies.Except(chosen).Select(d => d.ToolName).ToList();

        if (kept.Count > 0)
        {
            write($"fleet uninstall: kept {string.Join(", ", kept)} (installed by fleet's installer).");
        }

        foreach (var line in RemainsNote)
        {
            write(line);
        }

        return 0;
    }
}
