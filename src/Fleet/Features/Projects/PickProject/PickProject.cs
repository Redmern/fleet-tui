using Fleet.Ports.Projects;

namespace Fleet.Features.Projects.PickProject;

/// <summary>One row in the picker: either a saved project, or the "new" entry.</summary>
public sealed record PickerEntry(string Label, Project? Project)
{
    public bool IsNew => Project is null;
}

public sealed class PickProjectHandler(IProjectStore store)
{
    public const string NewLabel = "+  New project...";

    /// <summary>
    /// Saved projects followed by the "new project" entry, which is always last so
    /// its index is stable.
    /// </summary>
    public IReadOnlyList<PickerEntry> Entries()
    {
        var entries = store.List()
            .Select(p => new PickerEntry($"{p.Name}   {p.Root}", p))
            .ToList();

        entries.Add(new PickerEntry(NewLabel, null));
        return entries;
    }
}
