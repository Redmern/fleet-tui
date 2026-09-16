using Fleet.Ports.Projects.Models;

namespace Fleet.Features.Projects.PickProject.Models;

public sealed record ProjectPick(Project Project, bool NewWindow);
