using Fleet.Features.Projects.PickProject.Models;
using Fleet.Ports.Projects;
using Fleet.Ui.Models;

namespace Fleet.Features.Projects.PickProject;

public sealed class PickProjectHandler(IProjectStore store)
{
    public const string NewLabel = "+  New project...";

    public IReadOnlyList<ProjectChoice> Entries()
    {
        var entries = store.List()
            .Select(p => new ProjectChoice(p.Name, p.Root, p))
            .ToList();

        entries.Add(new ProjectChoice(NewLabel, string.Empty, null));
        return entries;
    }

    public static IReadOnlyList<FleetRow> Rows(IReadOnlyList<ProjectChoice> entries)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var nameWidth = entries.Max(e => e.Label.Length);

        return
        [
            .. entries.Select(e => new FleetRow(
                [FleetSpan.Plain(e.Label.PadRight(nameWidth))],
                e.Detail.Length == 0 ? null : [FleetSpan.Muted($"{e.Detail} ")])),
        ];
    }
}
