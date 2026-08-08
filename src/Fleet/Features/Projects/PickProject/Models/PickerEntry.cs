using Fleet.Ports.Projects.Models;

namespace Fleet.Features.Projects.PickProject.Models;

public sealed record PickerEntry(string Label, Project? Project)
{
    public bool IsNew => Project is null;
}
