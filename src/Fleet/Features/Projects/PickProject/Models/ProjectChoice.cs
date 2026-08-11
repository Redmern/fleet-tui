using Fleet.Ports.Projects.Models;

namespace Fleet.Features.Projects.PickProject.Models;

public sealed record ProjectChoice(string Label, string Detail, Project? Project)
{
    public bool IsNew => Project is null;
}
