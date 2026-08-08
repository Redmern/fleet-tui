using Fleet.Features.Projects.PickProject.Models;
using Fleet.Ports.Projects;

namespace Fleet.Features.Projects.PickProject;

public sealed class PickProjectHandler(IProjectStore store)
{
    public const string NewLabel = "+  New project...";

    public IReadOnlyList<PickerEntry> Entries()
    {
        var entries = store.List()
            .Select(p => new PickerEntry($"{p.Name}   {p.Root}", p))
            .ToList();

        entries.Add(new PickerEntry(NewLabel, null));
        return entries;
    }
}
